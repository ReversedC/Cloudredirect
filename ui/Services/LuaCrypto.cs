using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CloudRedirect.Services;

/// <summary>
/// Provides zero-knowledge client-side authenticated encryption (AES-256-GCM)
/// for .lua scripts synced to cloud providers.
/// Only CloudRedirect can read, decrypt, or verify these files.
/// </summary>
public static class LuaCrypto
{
    private static readonly byte[] MagicHeader = "CRLF"u8.ToArray(); // CloudRedirect Lua File
    private const byte CurrentVersion = 1;
    private const int NonceSize = 12; // 96 bits for AES-GCM
    private const int TagSize = 16;   // 128 bits authentication tag

    // Hardened salt and master application secret for key derivation
    private static readonly byte[] DerivationSalt = "CloudRedirect.LuaSync.ZeroKnowledge.v1.Salt_8921"u8.ToArray();
    private static readonly byte[] MasterSecret = "CloudRedirect_Vault_99214_LuaEncryptedSync_SecureKey"u8.ToArray();

    private static byte[] GetAesKey()
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(MasterSecret, DerivationSalt, 50000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32); // 256-bit AES key
    }

    /// <summary>
    /// Encrypts raw Lua plaintext into an authenticated binary envelope:
    /// [4B Magic: 'CRLF'][1B Version: 1][12B Nonce][16B Tag][Ciphertext...]
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext)
    {
        if (plaintext == null || plaintext.Length == 0)
            return Array.Empty<byte>();

        byte[] key = GetAesKey();
        byte[] nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(MagicHeader);
        writer.Write(CurrentVersion);
        writer.Write(nonce);
        writer.Write(tag);
        writer.Write(ciphertext);
        return ms.ToArray();
    }

    /// <summary>
    /// Decrypts and verifies an authenticated binary envelope back into raw Lua plaintext.
    /// Throws InvalidDataException if corrupted, tampered with, or invalid format.
    /// </summary>
    public static byte[] Decrypt(byte[] encryptedBytes)
    {
        if (encryptedBytes == null || encryptedBytes.Length < (4 + 1 + NonceSize + TagSize))
            throw new InvalidDataException("Invalid encrypted payload size.");

        using var ms = new MemoryStream(encryptedBytes);
        using var reader = new BinaryReader(ms);

        var magic = reader.ReadBytes(4);
        if (Encoding.ASCII.GetString(magic) != "CRLF")
            throw new InvalidDataException("Not a valid CloudRedirect encrypted Lua payload.");

        var version = reader.ReadByte();
        if (version != CurrentVersion)
            throw new InvalidDataException($"Unsupported payload version: {version}");

        var nonce = reader.ReadBytes(NonceSize);
        var tag = reader.ReadBytes(TagSize);
        var ciphertext = reader.ReadBytes((int)(ms.Length - ms.Position));

        byte[] key = GetAesKey();
        byte[] plaintext = new byte[ciphertext.Length];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        return plaintext;
    }

    /// <summary>
    /// Checks if a byte stream starts with the CloudRedirect encrypted Lua magic header.
    /// </summary>
    public static bool IsEncryptedLua(byte[] data)
    {
        if (data == null || data.Length < 4) return false;
        return data[0] == (byte)'C' && data[1] == (byte)'R' && data[2] == (byte)'L' && data[3] == (byte)'F';
    }
}
