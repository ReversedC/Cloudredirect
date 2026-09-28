package com.cloudredirect.gamehub;

import android.content.Context;
import android.content.SharedPreferences;
import android.view.KeyEvent;
import org.json.JSONArray;
import org.json.JSONObject;
import java.util.ArrayList;
import java.util.List;

public class HudConfig {
    public static final String PREFS_NAME = "GameHubHudPrefs";
    public static final String KEY_PRESET = "hud_preset";
    public static final String KEY_OPACITY = "hud_opacity";
    public static final String KEY_HAPTICS = "hud_haptics";
    public static final String KEY_PILL_X = "pill_x";
    public static final String KEY_PILL_Y = "pill_y";
    public static final String KEY_CUSTOM_ITEMS = "custom_items_json";

    public static final String PRESET_STEAM_LINK = "Steam Link Universal";
    public static final String PRESET_ACTION_RPG = "Action RPG";
    public static final String PRESET_FPS = "FPS Shooter";

    public static final int KEYCODE_MOUSE_LEFT = -1;
    public static final int KEYCODE_MOUSE_RIGHT = -2;

    public static class ControlDef {
        public String id;
        public String type; // "stick", "dpad", "btn"
        public String label;
        public int color;
        public int keyCode;
        public float xRatio; // 0.0 - 1.0 of screen width
        public float yRatio; // 0.0 - 1.0 of screen height
        public int sizeDp;

        public ControlDef(String id, String type, String label, int color, int keyCode, float xRatio, float yRatio, int sizeDp) {
            this.id = id;
            this.type = type;
            this.label = label;
            this.color = color;
            this.keyCode = keyCode;
            this.xRatio = xRatio;
            this.yRatio = yRatio;
            this.sizeDp = sizeDp;
        }

        public ControlDef(String id, String type, String label, int color, float xRatio, float yRatio, int sizeDp) {
            this(id, type, label, color, 0, xRatio, yRatio, sizeDp);
        }

        public JSONObject toJson() {
            try {
                JSONObject obj = new JSONObject();
                obj.put("id", id);
                obj.put("type", type);
                obj.put("label", label);
                obj.put("color", color);
                obj.put("keyCode", keyCode);
                obj.put("xRatio", (double) xRatio);
                obj.put("yRatio", (double) yRatio);
                obj.put("sizeDp", sizeDp);
                return obj;
            } catch (Exception e) {
                return null;
            }
        }

        public static ControlDef fromJson(JSONObject obj) {
            try {
                return new ControlDef(
                    obj.getString("id"),
                    obj.getString("type"),
                    obj.getString("label"),
                    obj.optInt("color", 0xFF66C0F4),
                    obj.optInt("keyCode", 0),
                    (float) obj.getDouble("xRatio"),
                    (float) obj.getDouble("yRatio"),
                    obj.getInt("sizeDp")
                );
            } catch (Exception e) {
                return null;
            }
        }
    }

    public static SharedPreferences getPrefs(Context context) {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
    }

    public static float getOpacity(Context context) {
        int level = getPrefs(context).getInt(KEY_OPACITY, 1); // 0=90%, 1=70%, 2=45%, 3=25%
        switch (level) {
            case 0: return 0.90f;
            case 1: return 0.70f;
            case 2: return 0.45f;
            case 3: return 0.25f;
            default: return 0.70f;
        }
    }

    public static boolean isHapticsEnabled(Context context) {
        return getPrefs(context).getBoolean(KEY_HAPTICS, true);
    }

    public static List<ControlDef> getControls(Context context) {
        String json = getPrefs(context).getString(KEY_CUSTOM_ITEMS, null);
        if (json != null) {
            try {
                JSONArray arr = new JSONArray(json);
                List<ControlDef> list = new ArrayList<ControlDef>();
                for (int i = 0; i < arr.length(); i++) {
                    ControlDef def = ControlDef.fromJson(arr.getJSONObject(i));
                    if (def != null) list.add(def);
                }
                if (!list.isEmpty()) return list;
            } catch (Exception ignored) {}
        }

        String preset = getPrefs(context).getString(KEY_PRESET, PRESET_STEAM_LINK);
        return getDefaultPresetControls(preset);
    }

    public static void saveControls(Context context, List<ControlDef> list) {
        try {
            JSONArray arr = new JSONArray();
            for (ControlDef def : list) {
                JSONObject obj = def.toJson();
                if (obj != null) arr.put(obj);
            }
            getPrefs(context).edit().putString(KEY_CUSTOM_ITEMS, arr.toString()).apply();
        } catch (Exception ignored) {}
    }

    public static void resetControls(Context context) {
        getPrefs(context).edit().remove(KEY_CUSTOM_ITEMS).apply();
    }

    public static List<ControlDef> getDefaultPresetControls(String preset) {
        List<ControlDef> list = new ArrayList<ControlDef>();

        if (PRESET_ACTION_RPG.equals(preset)) {
            // Left: Analog Joystick (WASD) + D-pad
            list.add(new ControlDef("stick_left", "stick", "MOVE", 0xFF00D2FF, 0, 0.14f, 0.68f, 135));
            list.add(new ControlDef("btn_tab", "btn", "TAB", 0xFF2A475E, KeyEvent.KEYCODE_TAB, 0.08f, 0.28f, 50));
            list.add(new ControlDef("btn_m", "btn", "MAP", 0xFF2A475E, KeyEvent.KEYCODE_M, 0.17f, 0.28f, 50));
            list.add(new ControlDef("btn_esc", "btn", "ESC", 0xFF1B2838, KeyEvent.KEYCODE_ESCAPE, 0.07f, 0.12f, 44));

            // Number Slots (1, 2, 3, 4) in upper center
            list.add(new ControlDef("btn_1", "btn", "1", 0xFF1A9FFF, KeyEvent.KEYCODE_1, 0.38f, 0.12f, 42));
            list.add(new ControlDef("btn_2", "btn", "2", 0xFF1A9FFF, KeyEvent.KEYCODE_2, 0.46f, 0.12f, 42));
            list.add(new ControlDef("btn_3", "btn", "3", 0xFF1A9FFF, KeyEvent.KEYCODE_3, 0.54f, 0.12f, 42));
            list.add(new ControlDef("btn_4", "btn", "4", 0xFF1A9FFF, KeyEvent.KEYCODE_4, 0.62f, 0.12f, 42));

            // Right: Action buttons (E interact, F use, Space jump, Shift sprint, Q skill, R reload)
            list.add(new ControlDef("btn_e", "btn", "E", 0xFF00D2FF, KeyEvent.KEYCODE_E, 0.86f, 0.54f, 58));
            list.add(new ControlDef("btn_f", "btn", "F", 0xFF5CB85C, KeyEvent.KEYCODE_F, 0.77f, 0.44f, 56));
            list.add(new ControlDef("btn_space", "btn", "SPACE", 0xFF00D2FF, KeyEvent.KEYCODE_SPACE, 0.84f, 0.76f, 70));
            list.add(new ControlDef("btn_shift", "btn", "SHIFT", 0xFF2A8FD4, KeyEvent.KEYCODE_SHIFT_LEFT, 0.72f, 0.68f, 56));
            list.add(new ControlDef("btn_q", "btn", "Q", 0xFFE5A823, KeyEvent.KEYCODE_Q, 0.86f, 0.36f, 52));
            list.add(new ControlDef("btn_r", "btn", "R", 0xFFD9534F, KeyEvent.KEYCODE_R, 0.93f, 0.44f, 52));
            return list;
        }

        if (PRESET_FPS.equals(preset)) {
            // Left: Move Joystick (WASD)
            list.add(new ControlDef("stick_left", "stick", "MOVE", 0xFF00D2FF, 0, 0.14f, 0.68f, 135));
            list.add(new ControlDef("btn_shift", "btn", "SPRINT", 0xFF2A8FD4, KeyEvent.KEYCODE_SHIFT_LEFT, 0.12f, 0.30f, 56));
            list.add(new ControlDef("btn_c", "btn", "CROUCH", 0xFF2A475E, KeyEvent.KEYCODE_C, 0.22f, 0.30f, 52));

            // Right: Aim Stick (Look) + Fire / Aim buttons
            list.add(new ControlDef("stick_right", "stick", "AIM", 0xFF00D2FF, 0, 0.72f, 0.70f, 125));
            list.add(new ControlDef("btn_fire", "btn", "FIRE", 0xFFD9534F, KEYCODE_MOUSE_LEFT, 0.90f, 0.48f, 66));
            list.add(new ControlDef("btn_aim", "btn", "ADS", 0xFF1A9FFF, KEYCODE_MOUSE_RIGHT, 0.82f, 0.34f, 56));
            list.add(new ControlDef("btn_space", "btn", "JUMP", 0xFF00D2FF, KeyEvent.KEYCODE_SPACE, 0.88f, 0.76f, 62));
            list.add(new ControlDef("btn_r", "btn", "RELOAD", 0xFFE5A823, KeyEvent.KEYCODE_R, 0.94f, 0.34f, 52));
            list.add(new ControlDef("btn_e", "btn", "USE", 0xFF5CB85C, KeyEvent.KEYCODE_E, 0.74f, 0.46f, 52));
            list.add(new ControlDef("btn_q", "btn", "SKILL", 0xFF2A8FD4, KeyEvent.KEYCODE_Q, 0.88f, 0.20f, 48));
            return list;
        }

        // PRESET_STEAM_LINK (Standard Full Gamepad)
        // Left Joystick (Bottom Left)
        list.add(new ControlDef("stick_left", "stick", "L-STICK", 0xFF00D2FF, 0, 0.14f, 0.70f, 130));

        // D-Pad (Mid Left)
        list.add(new ControlDef("dpad", "dpad", "D-PAD", 0xFF66C0F4, 0, 0.14f, 0.36f, 110));

        // Action Buttons A, B, X, Y (Bottom Right - Xbox / Steam standard layout)
        list.add(new ControlDef("btn_a", "btn", "A", 0xFF5C9E10, KeyEvent.KEYCODE_BUTTON_A, 0.88f, 0.75f, 56));
        list.add(new ControlDef("btn_b", "btn", "B", 0xFFD83B3B, KeyEvent.KEYCODE_BUTTON_B, 0.94f, 0.65f, 56));
        list.add(new ControlDef("btn_x", "btn", "X", 0xFF2A8FD4, KeyEvent.KEYCODE_BUTTON_X, 0.82f, 0.65f, 56));
        list.add(new ControlDef("btn_y", "btn", "Y", 0xFFE5A823, KeyEvent.KEYCODE_BUTTON_Y, 0.88f, 0.55f, 56));

        // Right Joystick / Camera Stick (Mid Right)
        list.add(new ControlDef("stick_right", "stick", "R-STICK", 0xFF00D2FF, 0, 0.74f, 0.72f, 120));

        // Bumpers & Triggers (Top Left / Right)
        list.add(new ControlDef("btn_lb", "btn", "LB", 0xFF355675, KeyEvent.KEYCODE_BUTTON_L1, 0.12f, 0.14f, 52));
        list.add(new ControlDef("btn_lt", "btn", "LT", 0xFF233B52, KEYCODE_MOUSE_RIGHT, 0.22f, 0.14f, 52));
        list.add(new ControlDef("btn_rt", "btn", "RT", 0xFF233B52, KEYCODE_MOUSE_LEFT, 0.78f, 0.14f, 52));
        list.add(new ControlDef("btn_rb", "btn", "RB", 0xFF355675, KeyEvent.KEYCODE_BUTTON_R1, 0.88f, 0.14f, 52));

        // System Buttons (Top Center)
        list.add(new ControlDef("btn_select", "btn", "VIEW", 0xFF1C2C3D, KeyEvent.KEYCODE_BUTTON_SELECT, 0.42f, 0.12f, 44));
        list.add(new ControlDef("btn_steam", "btn", "STEAM", 0xFF00D2FF, KeyEvent.KEYCODE_BUTTON_MODE, 0.50f, 0.12f, 48));
        list.add(new ControlDef("btn_start", "btn", "MENU", 0xFF1C2C3D, KeyEvent.KEYCODE_BUTTON_START, 0.58f, 0.12f, 44));

        return list;
    }
}
