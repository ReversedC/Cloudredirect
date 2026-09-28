package com.cloudredirect.gamehub;

import android.content.Context;
import android.content.SharedPreferences;
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

    public static class ControlDef {
        public String id;
        public String type; // "stick", "dpad", "btn"
        public String label;
        public int color;
        public float xRatio; // 0.0 - 1.0 of screen width
        public float yRatio; // 0.0 - 1.0 of screen height
        public int sizeDp;

        public ControlDef(String id, String type, String label, int color, float xRatio, float yRatio, int sizeDp) {
            this.id = id;
            this.type = type;
            this.label = label;
            this.color = color;
            this.xRatio = xRatio;
            this.yRatio = yRatio;
            this.sizeDp = sizeDp;
        }

        public JSONObject toJson() {
            try {
                JSONObject obj = new JSONObject();
                obj.put("id", id);
                obj.put("type", type);
                obj.put("label", label);
                obj.put("color", color);
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

        // Left Joystick (Bottom Left)
        list.add(new ControlDef("stick_left", "stick", "L-STICK", 0xFF00D2FF, 0.14f, 0.70f, 130));

        // D-Pad (Mid Left)
        list.add(new ControlDef("dpad", "dpad", "D-PAD", 0xFF66C0F4, 0.14f, 0.36f, 110));

        // Action Buttons A, B, X, Y (Bottom Right - Xbox / Steam standard layout)
        list.add(new ControlDef("btn_a", "btn", "A", 0xFF5C9E10, 0.88f, 0.75f, 56)); // Green
        list.add(new ControlDef("btn_b", "btn", "B", 0xFFD83B3B, 0.94f, 0.65f, 56)); // Red
        list.add(new ControlDef("btn_x", "btn", "X", 0xFF2A8FD4, 0.82f, 0.65f, 56)); // Blue
        list.add(new ControlDef("btn_y", "btn", "Y", 0xFFE5A823, 0.88f, 0.55f, 56)); // Yellow

        // Right Joystick / Camera Stick (Mid Right)
        list.add(new ControlDef("stick_right", "stick", "R-STICK", 0xFF00D2FF, 0.74f, 0.72f, 120));

        // Bumpers & Triggers (Top Left / Right)
        list.add(new ControlDef("btn_lb", "btn", "LB", 0xFF355675, 0.12f, 0.14f, 52));
        list.add(new ControlDef("btn_lt", "btn", "LT", 0xFF233B52, 0.22f, 0.14f, 52));
        list.add(new ControlDef("btn_rt", "btn", "RT", 0xFF233B52, 0.78f, 0.14f, 52));
        list.add(new ControlDef("btn_rb", "btn", "RB", 0xFF355675, 0.88f, 0.14f, 52));

        // System Buttons (Top Center)
        list.add(new ControlDef("btn_select", "btn", "VIEW", 0xFF1C2C3D, 0.42f, 0.12f, 44));
        list.add(new ControlDef("btn_steam", "btn", "STEAM", 0xFF00D2FF, 0.50f, 0.12f, 48));
        list.add(new ControlDef("btn_start", "btn", "MENU", 0xFF1C2C3D, 0.58f, 0.12f, 44));

        return list;
    }
}
