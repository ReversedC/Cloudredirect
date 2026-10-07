import os
import sys
import struct
import binascii
from pathlib import Path

# Standard generic Millennium STAR plugin loader bootstrap shim
STAR_SHIM_LUA = """
MILLENNIUM_DECOMPRESS = MILLENNIUM_DECOMPRESS
local bit = require("bit")
local bxor = bit.bxor
local band = bit.band

local XOR_KEY = 0x4D
local PARITY_SEED = 0xA7C3E91F
local STRIDE = 64
local HEADER_SIZE = 12
local ENTRY_SIZE = 256
local META_FLAG_DEFERRED = 0x0001
local SEC_METADATA, SEC_BACKEND = 0x01, 0x02
local SEC_FRONTEND, SEC_WEBKIT = 0x03, 0x04

local function bxor32(a, b)
local r = bxor(a, b)
return r < 0 and r + 4294967296 or r
end

local function u16_le(s, i)
return s:byte(i) + s:byte(i + 1) * 256
end

local function u32_le(s, i)
return s:byte(i) + s:byte(i + 1) * 256 + s:byte(i + 2) * 65536 + s:byte(i + 3) * 16777216
end

local function u64_le(s, i)
return u32_le(s, i) + u32_le(s, i + 4) * 4294967296
end

local function xor_decode(s)
local t = {}
for i = 1, #s do
t[i] = string.char(bxor32(bxor32(s:byte(i), XOR_KEY), (i - 1) % 256))
end
return table.concat(t)
end

local function fnv1a_step(hash, b)
local h = bxor32(hash, b)
local hl = h % 65536
local hh = math.floor(h / 65536)
local lo = hl * 403
local mid = hh * 403 + hl * 256 + math.floor(lo / 65536)
return (lo % 65536) + (mid % 65536) * 65536
end

local function fnv1a_u32(state, value)
state = fnv1a_step(state, value % 256)
state = fnv1a_step(state, math.floor(value / 256) % 256)
state = fnv1a_step(state, math.floor(value / 65536) % 256)
state = fnv1a_step(state, math.floor(value / 16777216) % 256)
return state
end

local function strip_parity(woven)
if #woven == 0 then return "" end
local out, rolling, idx, pos = {}, PARITY_SEED, 0, 1
while pos <= #woven do
assert(#woven - pos + 1 >= 12, "truncated parity stream at block " .. idx)
local de = math.min(pos + STRIDE - 1, #woven - 12)
local chunk = woven:sub(pos, de)
local pb = woven:sub(de + 1, de + 12)

local s_xor = u32_le(pb, 1)
local s_roll = u32_le(pb, 5)
local s_idx = u32_le(pb, 9)

local xc = 0
for j = 1, #chunk do xc = bxor32(xc, chunk:byte(j)) end

local e_roll = fnv1a_u32(rolling, xc)

assert(s_xor == xc, "parity xor mismatch at block " .. idx)
assert(s_roll == e_roll, "parity chain broken at block " .. idx)
assert(s_idx == idx, "parity block reordered at block " .. idx)

out[#out + 1] = chunk
rolling, idx, pos = e_roll, idx + 1, de + 13
end
return table.concat(out)
end

local function decompress(data)
assert(type(MILLENNIUM_DECOMPRESS) == "function", "MILLENNIUM_DECOMPRESS global not found")
return MILLENNIUM_DECOMPRESS(data)
end

local function decode_section(raw, encode_flags)
local d = (encode_flags % 128 >= 64) and xor_decode(raw) or raw
d = strip_parity(d)
if math.floor(encode_flags / 128) % 2 == 1 then
d = decompress(d)
end
return d
end

local function parse_sub_entries(data)
if not data or #data < 4 then return {} end
local count, files, pos = u32_le(data, 1), {}, 5
for _ = 1, count do
local nl = u16_le(data, pos)
local dl = u32_le(data, pos + 2)
pos = pos + 6
files[#files + 1] = {
name = data:sub(pos, pos + nl - 1),
data = data:sub(pos + nl, pos + nl + dl - 1),
}
pos = pos + nl + dl
end
return files
end

return function(path)
local f = assert(io.open(path, "rb"), "cannot open " .. path)

local len_raw = f:read(4)
local shim_len = u32_le(len_raw, 1)
f:seek("set", 4 + shim_len)
local hdr_raw = f:read(HEADER_SIZE)
assert(hdr_raw:sub(1, 4) == "STAR", "invalid .star magic")
local ver_maj = hdr_raw:byte(5)
local ver_min = hdr_raw:byte(6)
local section_count = hdr_raw:byte(7)

local tbl_raw = f:read(section_count * ENTRY_SIZE)
local sections, max_eager_end = {}, 0
for i = 0, section_count - 1 do
local b = i * ENTRY_SIZE + 1
local meta_flags = u32_le(tbl_raw, b + 4)
local offset = u64_le(tbl_raw, b + 8)
local length = u64_le(tbl_raw, b + 16)
sections[#sections + 1] = {
id = tbl_raw:byte(b),
encode_flags = tbl_raw:byte(b + 1),
meta_flags = meta_flags,
offset = offset,
length = length,
crc32 = u32_le(tbl_raw, b + 24),
}
if band(meta_flags, META_FLAG_DEFERRED) == 0 then
local section_end = offset + length
if section_end > max_eager_end then
max_eager_end = section_end
end
end
end

f:seek("set", 0)
local bytes = f:read(4 + shim_len + max_eager_end)
f:close()

local plg = 4 + shim_len + 1

local function read_section(id)
for _, s in ipairs(sections) do
if s.id == id then
local start_pos = plg + s.offset
local end_pos = plg + s.offset + s.length - 1
if end_pos > #bytes then return nil end
local raw = bytes:sub(start_pos, end_pos)
return decode_section(raw, s.encode_flags)
end
end
end

return {
version = { major = ver_maj, minor = ver_min },
sections = sections,
metadata_raw = function() return read_section(SEC_METADATA) end,
backend = function() return parse_sub_entries(read_section(SEC_BACKEND)) end,
frontend = function() return parse_sub_entries(read_section(SEC_FRONTEND)) end,
webkit = function() return parse_sub_entries(read_section(SEC_WEBKIT)) end,
}
end
"""

def bxor32(a, b):
    return (a ^ b) & 0xffffffff

def fnv1a_step(h, b):
    h = bxor32(h, b)
    hl = h % 65536
    hh = h // 65536
    lo = (hl * 403)
    mid = (hh * 403 + hl * 256 + (lo // 65536))
    return ((lo % 65536) + (mid % 65536) * 65536) & 0xffffffff

def fnv1a_u32(state, val):
    state = fnv1a_step(state, val & 0xff)
    state = fnv1a_step(state, (val >> 8) & 0xff)
    state = fnv1a_step(state, (val >> 16) & 0xff)
    state = fnv1a_step(state, (val >> 24) & 0xff)
    return state

def add_parity(data):
    out = bytearray()
    rolling = 0xA7C3E91F
    idx = 0
    pos = 0
    stride = 64
    dlen = len(data)
    while pos < dlen:
        chunk = data[pos : min(pos + stride, dlen)]
        xc = 0
        for b in chunk: xc ^= b
        e_roll = fnv1a_u32(rolling, xc)
        pb = struct.pack('<III', xc, e_roll, idx)
        out.extend(chunk)
        out.extend(pb)
        rolling = e_roll
        idx += 1
        pos += len(chunk)
    return bytes(out)

def xor_encode_decode(s):
    res = bytearray()
    for i, b in enumerate(s):
        res.append(((b ^ 0x4D) ^ (i % 256)) & 0xff)
    return bytes(res)

def lz4_compress(data):
    n = len(data)
    out = bytearray(struct.pack('<I', n))
    if n == 0:
        return bytes(out)
    
    hash_table = {}
    pos = 0
    anchor = 0
    
    while pos < n - 4:
        seq = data[pos : pos + 4]
        match_pos = hash_table.get(seq)
        hash_table[seq] = pos
        
        offset = pos - match_pos if match_pos is not None else 0
        if match_pos is not None and 0 < offset < 65536 and data[match_pos:match_pos+4] == seq:
            match_len = 4
            while pos + match_len < n and data[pos + match_len] == data[match_pos + match_len]:
                match_len += 1
            
            lit_len = pos - anchor
            token_lit = min(lit_len, 15)
            token_match = min(match_len - 4, 15)
            token = (token_lit << 4) | token_match
            out.append(token)
            
            if lit_len >= 15:
                rem = lit_len - 15
                while rem >= 255:
                    out.append(255)
                    rem -= 255
                out.append(rem)
            
            out.extend(data[anchor:pos])
            out.extend(struct.pack('<H', offset))
            
            if match_len - 4 >= 15:
                rem = (match_len - 4) - 15
                while rem >= 255:
                    out.append(255)
                    rem -= 255
                out.append(rem)
            
            pos += match_len
            anchor = pos
            continue
        
        pos += 1
    
    lit_len = n - anchor
    if lit_len > 0:
        token_lit = min(lit_len, 15)
        out.append(token_lit << 4)
        if lit_len >= 15:
            rem = lit_len - 15
            while rem >= 255:
                out.append(255)
                rem -= 255
                out.append(rem)
        out.extend(data[anchor:n])
    
    return bytes(out)

def encode_section_payload(raw_data):
    compressed = lz4_compress(raw_data)
    woven = add_parity(compressed)
    encoded = xor_encode_decode(woven)
    crc = binascii.crc32(encoded) & 0xffffffff
    return encoded, crc

def msgpack_encode_map(kv_pairs):
    assert len(kv_pairs) <= 15
    out = bytearray([0x80 | len(kv_pairs)])
    for k, v in kv_pairs:
        for s in (k, v):
            sb = s.encode('utf-8')
            slen = len(sb)
            if slen <= 31:
                out.append(0xA0 | slen)
            elif slen <= 255:
                out.append(0xD9)
                out.append(slen)
            else:
                out.append(0xDA)
                out.extend(struct.pack('>H', slen))
            out.extend(sb)
    return bytes(out)

def build_sub_entries(file_tuples):
    out = bytearray(struct.pack('<I', len(file_tuples)))
    for name, data in file_tuples:
        nb = name.encode('utf-8')
        out.extend(struct.pack('<HI', len(nb), len(data)))
        out.extend(nb)
        out.extend(data)
    return bytes(out)

def create_star_package(src_dir, output_star_path):
    src_dir = Path(src_dir).resolve()
    output_star_path = Path(output_star_path).resolve()

    backend_main_lua_path = src_dir / 'backend' / 'main.lua'
    backend_main_lua = backend_main_lua_path.read_bytes() if backend_main_lua_path.exists() else b''
    
    frontend_js_path = src_dir / '.millennium' / 'Dist' / 'index.js'
    if not frontend_js_path.exists():
        frontend_js_path = src_dir / 'index.js'
    frontend_bundle_js = frontend_js_path.read_bytes() if frontend_js_path.exists() else b''
    
    webkit_js_path = src_dir / '.millennium' / 'Dist' / 'webkit.js'
    if not webkit_js_path.exists():
        webkit_js_path = src_dir / 'webkit.js'
    webkit_bundle_js = webkit_js_path.read_bytes() if webkit_js_path.exists() else b''

    shim_bytes = STAR_SHIM_LUA.replace('\r\n', '\n').replace('\n', '\r\n').encode('utf-8')

    meta_pairs = [
        ('id', 'CloudRedirect'),
        ('name', 'CloudRedirect'),
        ('version', '1.0.0'),
        ('author', 'CloudRedirect'),
        ('description', 'Native Steam integration for CloudRedirect cloud saves, taskbar progress, and SteamDB.'),
        ('starlight_version', '1.1.4'),
        ('entry', 'backend/main.lua'),
    ]
    sec1_raw = msgpack_encode_map(meta_pairs)

    backend_entries = []
    backend_dir = src_dir / 'backend'
    if backend_dir.exists():
        for p in sorted(backend_dir.glob('*.lua')):
            rel = 'backend/' + p.name
            backend_entries.append((rel, p.read_bytes()))
    sec2_raw = build_sub_entries(backend_entries)
    sec3_raw = build_sub_entries([('bundle.js', frontend_bundle_js)])
    sec4_raw = build_sub_entries([('bundle.js', webkit_bundle_js)])
    
    sections = [
        (1, sec1_raw), # SEC_METADATA
        (2, sec2_raw), # SEC_BACKEND
        (3, sec3_raw), # SEC_FRONTEND
        (4, sec4_raw), # SEC_WEBKIT
    ]
    
    HEADER_SIZE = 12
    ENTRY_SIZE = 256
    section_count = len(sections)
    table_size = section_count * ENTRY_SIZE
    
    current_offset = HEADER_SIZE + table_size
    encoded_sections = []
    
    for sid, raw in sections:
        encoded, crc = encode_section_payload(raw)
        encoded_sections.append({
            'id': sid,
            'encode_flags': 192,
            'meta_flags': 0,
            'offset': current_offset,
            'length': len(encoded),
            'crc32': crc,
            'payload': encoded,
            'raw_len': len(raw)
        })
        current_offset += len(encoded)
    
    star_container = bytearray()
    star_container.extend(b'STAR\x02\x00' + bytes([section_count]) + b'\x00'*5)
    
    for s in encoded_sections:
        entry = bytearray(ENTRY_SIZE)
        entry[0] = s['id']
        entry[1] = s['encode_flags']
        struct.pack_into('<I', entry, 4, s['meta_flags'])
        struct.pack_into('<Q', entry, 8, s['offset'])
        struct.pack_into('<Q', entry, 16, s['length'])
        struct.pack_into('<I', entry, 24, s['crc32'])
        star_container.extend(entry)
    
    for s in encoded_sections:
        star_container.extend(s['payload'])
    
    final_output = bytearray(struct.pack('<I', len(shim_bytes)))
    final_output.extend(shim_bytes)
    final_output.extend(star_container)
    
    output_star_path.parent.mkdir(parents=True, exist_ok=True)
    output_star_path.write_bytes(final_output)
    
    print(f"Successfully packaged: {output_star_path} ({len(final_output)} bytes)")
    
    # Also mirror into local Steam Millennium plugins directory if it exists
    steam_plugin_dir = Path(r"C:\Program Files (x86)\Steam\millennium\plugins")
    if steam_plugin_dir.exists():
        try:
            (steam_plugin_dir / output_star_path.name).write_bytes(final_output)
            print(f"Mirrored to Steam: {steam_plugin_dir / output_star_path.name}")
        except Exception as e:
            print(f"Note: Could not mirror to Steam plugins folder ({e})")

if __name__ == '__main__':
    repo_root = Path(__file__).resolve().parent.parent
    src = repo_root / 'ui' / 'Resources' / 'MillenniumPlugin'
    out = src / 'CloudRedirect.star'
    create_star_package(src, out)
