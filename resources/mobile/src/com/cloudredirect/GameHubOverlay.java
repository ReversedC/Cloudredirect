package com.cloudredirect;

import android.app.AlertDialog;
import android.content.Context;
import android.content.DialogInterface;
import android.content.SharedPreferences;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.InputMethodManager;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;
import com.limelight.Game;
import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

public class GameHubOverlay {
    private static final String PREFS_NAME = "CloudRedirectGameHub_v3";
    private static final String KEY_PILL_X = "pill_x";
    private static final String KEY_PILL_Y = "pill_y";
    private static final String KEY_CONTROLS_JSON = "custom_controls_v3";
    private static final String KEY_GAMEPAD_VISIBLE = "standard_gamepad_visible";

    // Steam Colors (Translucent for mobile HUD)
    private static final int COLOR_STEAM_BG = 0x66101721;
    private static final int COLOR_STEAM_BG_ACTIVE = 0xCC00D2FF;
    private static final int COLOR_STEAM_BORDER = 0x9900D2FF;
    private static final int COLOR_STEAM_CYAN = 0xFF00D2FF;
    private static final int COLOR_TEXT_WHITE = 0xFFFFFFFF;
    private static final int COLOR_DELETE_RED = 0xEECC3333;

    // Special Mouse KeyCodes
    private static final int KEYCODE_MOUSE_LEFT = -1;
    private static final int KEYCODE_MOUSE_RIGHT = -2;

    public static class ControlItem {
        public String id;
        public String type; // "button", "dpad", "joystick"
        public String label;
        public int keyCode;
        public float xRatio;
        public float yRatio;
        public int sizeDp;

        public ControlItem(String id, String type, String label, int keyCode, float xRatio, float yRatio, int sizeDp) {
            this.id = id;
            this.type = type;
            this.label = label;
            this.keyCode = keyCode;
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
                obj.put("keyCode", keyCode);
                obj.put("xRatio", (double) xRatio);
                obj.put("yRatio", (double) yRatio);
                obj.put("sizeDp", sizeDp);
                return obj;
            } catch (Exception e) {
                return null;
            }
        }

        public static ControlItem fromJson(JSONObject obj) {
            try {
                String id = obj.optString("id", "item_" + System.currentTimeMillis());
                String type = obj.optString("type", "button");
                String label = obj.optString("label", "BTN");
                int keyCode = obj.optInt("keyCode", KeyEvent.KEYCODE_E);
                float xRatio = (float) obj.optDouble("xRatio", 0.5f);
                float yRatio = (float) obj.optDouble("yRatio", 0.5f);
                int sizeDp = obj.optInt("sizeDp", 54);
                return new ControlItem(id, type, label, keyCode, xRatio, yRatio, sizeDp);
            } catch (Exception e) {
                return null;
            }
        }
    }

    private final Game game;
    private final FrameLayout rootLayout;
    private final Vibrator vibrator;
    private final SharedPreferences prefs;

    private TextView floatingButton;
    private LinearLayout editToolbar;
    private FrameLayout controlsContainer;

    private boolean isEditMode = false;
    private boolean isStandardGamepadVisible = true;
    private int currentOpacityLevel = 0; // 0 = 85%, 1 = 55%, 2 = 30%

    private final List<ControlItem> controlItems = new ArrayList<ControlItem>();

    public static void attach(final Game game) {
        if (game == null) return;
        game.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    new GameHubOverlay(game).init();
                } catch (Throwable t) {
                    t.printStackTrace();
                }
            }
        });
    }

    private GameHubOverlay(Game game) {
        this.game = game;
        this.prefs = game.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        this.vibrator = (Vibrator) game.getSystemService(Context.VIBRATOR_SERVICE);

        View decor = game.getWindow().getDecorView();
        if (decor instanceof FrameLayout) {
            this.rootLayout = (FrameLayout) decor;
        } else {
            View content = game.findViewById(android.R.id.content);
            if (content instanceof FrameLayout) {
                this.rootLayout = (FrameLayout) content;
            } else {
                this.rootLayout = (FrameLayout) decor;
            }
        }
    }

    private void init() {
        controlsContainer = new FrameLayout(game);
        rootLayout.addView(controlsContainer, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT));

        createFloatingButton();
        createEditToolbar();

        loadSavedControls();

        isStandardGamepadVisible = prefs.getBoolean(KEY_GAMEPAD_VISIBLE, true);

        rootLayout.post(new Runnable() {
            @Override
            public void run() {
                renderControls();
                // Check for updates on startup
                AutoUpdateService.checkOnLaunch(game);
            }
        });
    }

    private int dpToPx(int dp) {
        float density = game.getResources().getDisplayMetrics().density;
        return Math.round(dp * density);
    }

    private float dpToPx(float dp) {
        float density = game.getResources().getDisplayMetrics().density;
        return dp * density;
    }

    private GradientDrawable makePillBackground(int bgColor, int borderColor, int radiusDp, int strokeDp) {
        GradientDrawable gd = new GradientDrawable();
        gd.setShape(GradientDrawable.RECTANGLE);
        gd.setColor(bgColor);
        gd.setCornerRadius(dpToPx(radiusDp));
        if (strokeDp > 0) {
            gd.setStroke(dpToPx(strokeDp), borderColor);
        }
        return gd;
    }

    private void vibrateTick() {
        if (vibrator == null) return;
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                vibrator.vibrate(VibrationEffect.createPredefined(VibrationEffect.EFFECT_CLICK));
            } else {
                vibrator.vibrate(20);
            }
        } catch (Throwable ignored) {}
    }

    private void sendKey(boolean down, int keyCode) {
        try {
            if (keyCode == KEYCODE_MOUSE_LEFT) {
                game.mouseButtonEvent(1, down);
            } else if (keyCode == KEYCODE_MOUSE_RIGHT) {
                game.mouseButtonEvent(2, down);
            } else {
                game.keyboardEvent(down, (short) keyCode);
            }
        } catch (Throwable t) {
            t.printStackTrace();
        }
    }

    // ==========================================
    // PERSISTENCE
    // ==========================================
    private void loadSavedControls() {
        controlItems.clear();
        String json = prefs.getString(KEY_CONTROLS_JSON, null);
        if (json != null && !json.trim().isEmpty()) {
            try {
                JSONArray arr = new JSONArray(json);
                for (int i = 0; i < arr.length(); i++) {
                    JSONObject obj = arr.getJSONObject(i);
                    ControlItem item = ControlItem.fromJson(obj);
                    if (item != null) {
                        controlItems.add(item);
                    }
                }
            } catch (Exception e) {
                e.printStackTrace();
            }
        }
        // Notice: By default, controlItems is EMPTY!
        // No duplicate D-pad or ABXY buttons layered over Moonlight's clean screen.
    }

    private void saveControls() {
        try {
            JSONArray arr = new JSONArray();
            for (ControlItem item : controlItems) {
                JSONObject obj = item.toJson();
                if (obj != null) {
                    arr.put(obj);
                }
            }
            prefs.edit().putString(KEY_CONTROLS_JSON, arr.toString()).apply();
        } catch (Exception e) {
            e.printStackTrace();
        }
    }

    // ==========================================
    // SLEEK FLOATING TRIGGER BUTTON (EDGE-DOCKED)
    // ==========================================
    private void createFloatingButton() {
        floatingButton = new TextView(game);
        floatingButton.setText("🎮");
        floatingButton.setTextSize(16);
        floatingButton.setGravity(Gravity.CENTER);
        floatingButton.setTextColor(COLOR_STEAM_CYAN);
        floatingButton.setBackground(makePillBackground(0x77101721, COLOR_STEAM_BORDER, 19, 1));

        int size = dpToPx(38);
        final FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(size, size);

        // Default: Top-right corner
        int defaultX = rootLayout.getWidth() > 0 ? (rootLayout.getWidth() - dpToPx(50)) : dpToPx(280);
        int defaultY = dpToPx(16);

        int savedX = prefs.getInt(KEY_PILL_X, defaultX);
        int savedY = prefs.getInt(KEY_PILL_Y, defaultY);
        lp.leftMargin = Math.max(0, savedX);
        lp.topMargin = Math.max(0, savedY);
        floatingButton.setLayoutParams(lp);

        floatingButton.setOnTouchListener(new View.OnTouchListener() {
            private float dX, dY;
            private float startX, startY;
            private boolean isDragging = false;

            @Override
            public boolean onTouch(View view, MotionEvent event) {
                switch (event.getActionMasked()) {
                    case MotionEvent.ACTION_DOWN:
                        startX = event.getRawX();
                        startY = event.getRawY();
                        dX = view.getX() - startX;
                        dY = view.getY() - startY;
                        isDragging = false;
                        floatingButton.setBackground(makePillBackground(0xCC00D2FF, 0xFFFFFFFF, 19, 2));
                        return true;

                    case MotionEvent.ACTION_MOVE:
                        float diffX = Math.abs(event.getRawX() - startX);
                        float diffY = Math.abs(event.getRawY() - startY);
                        if (diffX > dpToPx(6) || diffY > dpToPx(6)) {
                            isDragging = true;
                            float newX = event.getRawX() + dX;
                            float newY = event.getRawY() + dY;

                            int maxW = rootLayout.getWidth() - view.getWidth();
                            int maxH = rootLayout.getHeight() - view.getHeight();
                            if (maxW > 0) newX = Math.max(0, Math.min(newX, maxW));
                            if (maxH > 0) newY = Math.max(0, Math.min(newY, maxH));

                            view.setX(newX);
                            view.setY(newY);
                        }
                        return true;

                    case MotionEvent.ACTION_UP:
                    case MotionEvent.ACTION_CANCEL:
                        floatingButton.setBackground(makePillBackground(0x77101721, COLOR_STEAM_BORDER, 19, 1));
                        if (isDragging) {
                            prefs.edit().putInt(KEY_PILL_X, Math.round(view.getX()))
                                         .putInt(KEY_PILL_Y, Math.round(view.getY()))
                                         .apply();
                        } else {
                            vibrateTick();
                            if (isEditMode) {
                                exitEditMode();
                            } else {
                                showGameHubMenu();
                            }
                        }
                        return true;
                }
                return false;
            }
        });

        rootLayout.addView(floatingButton);
    }

    // ==========================================
    // GAMEHUB MODAL MENU (CLEAN, NATIVE DIALOG)
    // ==========================================
    private void showGameHubMenu() {
        String gamepadStatus = isStandardGamepadVisible ? "Disable Standard Gamepad" : "Enable Standard Gamepad";
        final String[] options = new String[]{
                "➕  Add Custom Button (E, F, Space...)",
                "➕  Add Custom D-Pad",
                "➕  Add Custom Joystick",
                "🎮  " + gamepadStatus,
                "📐  Move / Edit Button Layout",
                "🗑  Clear All Custom Buttons",
                "👁  Change Opacity (" + getOpacityLabel() + ")",
                "⌨  Toggle Soft Keyboard",
                "🔄  Check for Updates (v" + AutoUpdateService.APP_VERSION + ")",
                "✖  Close Menu"
        };

        AlertDialog.Builder b = new AlertDialog.Builder(game, android.R.style.Theme_DeviceDefault_Dialog_Alert);
        b.setTitle("🎮 CloudRedirect GameHub Controls");
        b.setItems(options, new DialogInterface.OnClickListener() {
            @Override
            public void onClick(DialogInterface dialog, int which) {
                switch (which) {
                    case 0: // Add Button
                        showAddButtonDialog();
                        break;
                    case 1: // Add D-Pad
                        addDPad();
                        break;
                    case 2: // Add Joystick
                        addJoystick();
                        break;
                    case 3: // Toggle Standard Gamepad
                        toggleStandardGamepad();
                        break;
                    case 4: // Edit Layout
                        enterEditMode();
                        break;
                    case 5: // Clear Custom
                        confirmClearAll();
                        break;
                    case 6: // Opacity
                        cycleOpacity();
                        break;
                    case 7: // Soft Keyboard
                        toggleSoftKeyboard();
                        break;
                    case 8: // Check for Updates
                        AutoUpdateService.checkManual(game);
                        break;
                    case 9: // Close
                        break;
                }
            }
        });
        b.setNegativeButton("Cancel", null);
        b.show();
    }

    private String getOpacityLabel() {
        switch (currentOpacityLevel) {
            case 1: return "55%";
            case 2: return "30%";
            default: return "85%";
        }
    }

    private void toggleStandardGamepad() {
        try {
            isStandardGamepadVisible = !isStandardGamepadVisible;
            prefs.edit().putBoolean(KEY_GAMEPAD_VISIBLE, isStandardGamepadVisible).apply();
            if (game.virtualController != null) {
                if (isStandardGamepadVisible) {
                    game.virtualController.show();
                } else {
                    game.virtualController.hide();
                }
            }
        } catch (Throwable ignored) {}
    }

    private void confirmClearAll() {
        AlertDialog.Builder b = new AlertDialog.Builder(game, android.R.style.Theme_DeviceDefault_Dialog_Alert);
        b.setTitle("Clear Custom Controls");
        b.setMessage("Remove all custom buttons from the screen?");
        b.setPositiveButton("Clear All", new DialogInterface.OnClickListener() {
            @Override
            public void onClick(DialogInterface dialog, int which) {
                controlItems.clear();
                saveControls();
                renderControls();
            }
        });
        b.setNegativeButton("Cancel", null);
        b.show();
    }

    // ==========================================
    // COMPACT EDIT TOOLBAR (ONLY IN EDIT MODE)
    // ==========================================
    private void createEditToolbar() {
        editToolbar = new LinearLayout(game);
        editToolbar.setOrientation(LinearLayout.HORIZONTAL);
        editToolbar.setGravity(Gravity.CENTER_VERTICAL);
        editToolbar.setBackground(makePillBackground(0xEE101721, COLOR_STEAM_CYAN, 8, 1));
        int pad = dpToPx(6);
        editToolbar.setPadding(pad * 2, pad, pad * 2, pad);
        editToolbar.setVisibility(View.GONE);

        FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT,
                ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
        lp.topMargin = dpToPx(8);
        editToolbar.setLayoutParams(lp);

        TextView title = new TextView(game);
        title.setText("DRAG TO REPOSITION");
        title.setTextSize(11);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        title.setTextColor(COLOR_STEAM_CYAN);
        editToolbar.addView(title);

        editToolbar.addView(createToolbarButton("+ BUTTON", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                showAddButtonDialog();
            }
        }));

        editToolbar.addView(createToolbarButton("💾 DONE", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                exitEditMode();
            }
        }));

        rootLayout.addView(editToolbar);
    }

    private View createToolbarButton(String label, final View.OnClickListener listener) {
        TextView btn = new TextView(game);
        btn.setText(label);
        btn.setTextSize(11);
        btn.setTypeface(Typeface.DEFAULT_BOLD);
        btn.setTextColor(COLOR_TEXT_WHITE);
        btn.setGravity(Gravity.CENTER);
        btn.setBackground(makePillBackground(0xFF1B2838, COLOR_STEAM_BORDER, 6, 1));
        int padH = dpToPx(10);
        int padV = dpToPx(5);
        btn.setPadding(padH, padV, padH, padV);

        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT,
                ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.leftMargin = dpToPx(8);
        btn.setLayoutParams(lp);

        btn.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                vibrateTick();
                if (listener != null) listener.onClick(v);
            }
        });

        return btn;
    }

    private void enterEditMode() {
        isEditMode = true;
        editToolbar.setVisibility(View.VISIBLE);
        floatingButton.setText("💾");
        floatingButton.setBackground(makePillBackground(0xCC5C7E10, 0xFF6CB42C, 19, 2));
        renderControls();
    }

    private void exitEditMode() {
        isEditMode = false;
        editToolbar.setVisibility(View.GONE);
        floatingButton.setText("🎮");
        floatingButton.setBackground(makePillBackground(0x77101721, COLOR_STEAM_BORDER, 19, 1));
        saveControls();
        renderControls();
    }

    private void toggleSoftKeyboard() {
        try {
            InputMethodManager imm = (InputMethodManager) game.getSystemService(Context.INPUT_METHOD_SERVICE);
            if (imm != null) {
                imm.toggleSoftInput(InputMethodManager.SHOW_FORCED, 0);
            }
        } catch (Throwable ignored) {}
    }

    private void cycleOpacity() {
        currentOpacityLevel = (currentOpacityLevel + 1) % 3;
        float alpha;
        switch (currentOpacityLevel) {
            case 1:
                alpha = 0.55f;
                break;
            case 2:
                alpha = 0.30f;
                break;
            case 0:
            default:
                alpha = 0.85f;
                break;
        }
        controlsContainer.setAlpha(alpha);
    }

    // ==========================================
    // ADD BUTTON PICKER
    // ==========================================
    private void showAddButtonDialog() {
        final String[] keys = new String[]{
                "E (Interact)", "F (Action / Use)", "SPACE (Jump)", "SHIFT (Sprint)", "CTRL (Crouch)",
                "TAB (Inventory)", "ESC (Menu)", "ENTER (Confirm)", "Q (Skill)", "R (Reload)",
                "C (Prone)", "V (Melee)", "Z (Cover)", "X (Special)", "G (Grenade)", "M (Map)",
                "LEFT MOUSE CLICK", "RIGHT MOUSE CLICK",
                "1 (Slot 1)", "2 (Slot 2)", "3 (Slot 3)", "4 (Slot 4)"
        };

        final int[] keyCodes = new int[]{
                KeyEvent.KEYCODE_E, KeyEvent.KEYCODE_F, KeyEvent.KEYCODE_SPACE, KeyEvent.KEYCODE_SHIFT_LEFT, KeyEvent.KEYCODE_CTRL_LEFT,
                KeyEvent.KEYCODE_TAB, KeyEvent.KEYCODE_ESCAPE, KeyEvent.KEYCODE_ENTER, KeyEvent.KEYCODE_Q, KeyEvent.KEYCODE_R,
                KeyEvent.KEYCODE_C, KeyEvent.KEYCODE_V, KeyEvent.KEYCODE_Z, KeyEvent.KEYCODE_X, KeyEvent.KEYCODE_G, KeyEvent.KEYCODE_M,
                KEYCODE_MOUSE_LEFT, KEYCODE_MOUSE_RIGHT,
                KeyEvent.KEYCODE_1, KeyEvent.KEYCODE_2, KeyEvent.KEYCODE_3, KeyEvent.KEYCODE_4
        };

        final String[] shortLabels = new String[]{
                "E", "F", "SPACE", "SHIFT", "CTRL",
                "TAB", "ESC", "ENTER", "Q", "R",
                "C", "V", "Z", "X", "G", "M",
                "L-CLK", "R-CLK",
                "1", "2", "3", "4"
        };

        AlertDialog.Builder builder = new AlertDialog.Builder(game, android.R.style.Theme_DeviceDefault_Dialog_Alert);
        builder.setTitle("Select Key for Custom Button");
        builder.setItems(keys, new DialogInterface.OnClickListener() {
            @Override
            public void onClick(DialogInterface dialog, int which) {
                String id = "btn_" + System.currentTimeMillis();
                int size = "SPACE".equals(shortLabels[which]) ? 66 : 54;
                controlItems.add(new ControlItem(id, "button", shortLabels[which], keyCodes[which], 0.5f, 0.5f, size));
                saveControls();
                renderControls();
                enterEditMode();
            }
        });
        builder.setNegativeButton("Cancel", null);
        builder.show();
    }

    private void addDPad() {
        String id = "dpad_" + System.currentTimeMillis();
        controlItems.add(new ControlItem(id, "dpad", "D-PAD", 0, 0.12f, 0.55f, 150));
        saveControls();
        renderControls();
        enterEditMode();
    }

    private void addJoystick() {
        String id = "joy_" + System.currentTimeMillis();
        controlItems.add(new ControlItem(id, "joystick", "JOY", 0, 0.12f, 0.55f, 140));
        saveControls();
        renderControls();
        enterEditMode();
    }

    // ==========================================
    // RENDER CONTROLS (SEMI-TRANSLUCENT NATIVE HUD)
    // ==========================================
    private void renderControls() {
        controlsContainer.removeAllViews();
        final int rootW = rootLayout.getWidth();
        final int rootH = rootLayout.getHeight();
        if (rootW <= 0 || rootH <= 0) return;

        for (final ControlItem item : controlItems) {
            final int sizePx = dpToPx(item.sizeDp);
            final FrameLayout wrapper = new FrameLayout(game);

            int x = Math.round(item.xRatio * rootW);
            int y = Math.round(item.yRatio * rootH);

            FrameLayout.LayoutParams wlp = new FrameLayout.LayoutParams(sizePx, sizePx);
            wlp.leftMargin = Math.max(0, Math.min(x, rootW - sizePx));
            wlp.topMargin = Math.max(0, Math.min(y, rootH - sizePx));
            wrapper.setLayoutParams(wlp);

            View controlView;
            if ("dpad".equals(item.type)) {
                controlView = new DPadView(game, item);
            } else if ("joystick".equals(item.type)) {
                controlView = new JoystickView(game, item);
            } else {
                controlView = new CustomButtonView(game, item);
            }
            wrapper.addView(controlView, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.MATCH_PARENT));

            if (isEditMode) {
                wrapper.setBackground(makePillBackground(0x3300D2FF, COLOR_STEAM_CYAN, 8, 2));

                // Delete button in top-right
                TextView delBtn = new TextView(game);
                delBtn.setText("✖");
                delBtn.setTextSize(10);
                delBtn.setTypeface(Typeface.DEFAULT_BOLD);
                delBtn.setTextColor(COLOR_TEXT_WHITE);
                delBtn.setGravity(Gravity.CENTER);
                delBtn.setBackground(makePillBackground(COLOR_DELETE_RED, COLOR_TEXT_WHITE, 10, 1));
                int delSize = dpToPx(20);
                FrameLayout.LayoutParams dlp = new FrameLayout.LayoutParams(delSize, delSize);
                dlp.gravity = Gravity.TOP | Gravity.RIGHT;
                delBtn.setLayoutParams(dlp);

                delBtn.setOnClickListener(new View.OnClickListener() {
                    @Override
                    public void onClick(View v) {
                        vibrateTick();
                        controlItems.remove(item);
                        saveControls();
                        renderControls();
                    }
                });
                wrapper.addView(delBtn);

                // Dragging in Edit Mode
                wrapper.setOnTouchListener(new View.OnTouchListener() {
                    private float startX, startY;
                    private float dX, dY;

                    @Override
                    public boolean onTouch(View v, MotionEvent event) {
                        switch (event.getActionMasked()) {
                            case MotionEvent.ACTION_DOWN:
                                startX = event.getRawX();
                                startY = event.getRawY();
                                dX = v.getX() - startX;
                                dY = v.getY() - startY;
                                return true;

                            case MotionEvent.ACTION_MOVE:
                                float newX = event.getRawX() + dX;
                                float newY = event.getRawY() + dY;
                                int maxW = rootW - v.getWidth();
                                int maxH = rootH - v.getHeight();
                                newX = Math.max(0, Math.min(newX, maxW));
                                newY = Math.max(0, Math.min(newY, maxH));
                                v.setX(newX);
                                v.setY(newY);
                                return true;

                            case MotionEvent.ACTION_UP:
                            case MotionEvent.ACTION_CANCEL:
                                item.xRatio = v.getX() / (float) rootW;
                                item.yRatio = v.getY() / (float) rootH;
                                saveControls();
                                return true;
                        }
                        return false;
                    }
                });
            }

            controlsContainer.addView(wrapper);
        }
    }

    // ==========================================
    // TRANSLUCENT CUSTOM BUTTON VIEW
    // ==========================================
    private class CustomButtonView extends View {
        private final ControlItem item;
        private final Paint bgPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint strokePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint textPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private boolean isDown = false;

        public CustomButtonView(Context context, ControlItem item) {
            super(context);
            this.item = item;

            bgPaint.setStyle(Paint.Style.FILL);
            strokePaint.setStyle(Paint.Style.STROKE);
            strokePaint.setStrokeWidth(dpToPx(1.5f));

            textPaint.setColor(COLOR_TEXT_WHITE);
            textPaint.setTypeface(Typeface.DEFAULT_BOLD);
            textPaint.setTextAlign(Paint.Align.CENTER);
        }

        @Override
        protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            float cx = getWidth() / 2f;
            float cy = getHeight() / 2f;
            float radius = Math.min(cx, cy) - dpToPx(2);

            bgPaint.setColor(isDown ? COLOR_STEAM_BG_ACTIVE : COLOR_STEAM_BG);
            strokePaint.setColor(isDown ? 0xFFFFFFFF : COLOR_STEAM_BORDER);

            canvas.drawCircle(cx, cy, radius, bgPaint);
            canvas.drawCircle(cx, cy, radius, strokePaint);

            textPaint.setTextSize(item.label.length() > 3 ? dpToPx(10) : dpToPx(16));
            Paint.FontMetrics fm = textPaint.getFontMetrics();
            float textY = cy - (fm.ascent + fm.descent) / 2f;
            canvas.drawText(item.label, cx, textY, textPaint);
        }

        @Override
        public boolean onTouchEvent(MotionEvent event) {
            if (isEditMode) return false;

            switch (event.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                    isDown = true;
                    vibrateTick();
                    invalidate();
                    sendKey(true, item.keyCode);
                    return true;

                case MotionEvent.ACTION_UP:
                case MotionEvent.ACTION_CANCEL:
                    isDown = false;
                    invalidate();
                    sendKey(false, item.keyCode);
                    return true;
            }
            return false;
        }
    }

    // ==========================================
    // TRANSLUCENT D-PAD VIEW
    // ==========================================
    private class DPadView extends View {
        private final ControlItem item;
        private final Paint bgPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint strokePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint textPaint = new Paint(Paint.ANTI_ALIAS_FLAG);

        private boolean upActive = false;
        private boolean downActive = false;
        private boolean leftActive = false;
        private boolean rightActive = false;

        public DPadView(Context context, ControlItem item) {
            super(context);
            this.item = item;

            bgPaint.setColor(COLOR_STEAM_BG);
            bgPaint.setStyle(Paint.Style.FILL);

            strokePaint.setColor(COLOR_STEAM_BORDER);
            strokePaint.setStyle(Paint.Style.STROKE);
            strokePaint.setStrokeWidth(dpToPx(1.5f));

            textPaint.setColor(COLOR_TEXT_WHITE);
            textPaint.setTextSize(dpToPx(13));
            textPaint.setTypeface(Typeface.DEFAULT_BOLD);
            textPaint.setTextAlign(Paint.Align.CENTER);
        }

        @Override
        protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            float cx = getWidth() / 2f;
            float cy = getHeight() / 2f;
            float r = Math.min(cx, cy) - dpToPx(2);

            canvas.drawCircle(cx, cy, r, bgPaint);
            canvas.drawCircle(cx, cy, r, strokePaint);

            canvas.drawText("W", cx, cy - r * 0.45f, textPaint);
            canvas.drawText("S", cx, cy + r * 0.65f, textPaint);
            canvas.drawText("A", cx - r * 0.55f, cy + dpToPx(5), textPaint);
            canvas.drawText("D", cx + r * 0.55f, cy + dpToPx(5), textPaint);
        }

        @Override
        public boolean onTouchEvent(MotionEvent event) {
            if (isEditMode) return false;

            float cx = getWidth() / 2f;
            float cy = getHeight() / 2f;

            switch (event.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                case MotionEvent.ACTION_MOVE:
                    float dx = event.getX() - cx;
                    float dy = event.getY() - cy;
                    float dist = (float) Math.sqrt(dx * dx + dy * dy);

                    boolean newUp = (dy < -dpToPx(12) && Math.abs(dy) > Math.abs(dx) * 0.5f);
                    boolean newDown = (dy > dpToPx(12) && Math.abs(dy) > Math.abs(dx) * 0.5f);
                    boolean newLeft = (dx < -dpToPx(12) && Math.abs(dx) > Math.abs(dy) * 0.5f);
                    boolean newRight = (dx > dpToPx(12) && Math.abs(dx) > Math.abs(dy) * 0.5f);

                    if (dist > dpToPx(8)) {
                        updateKey(upActive != newUp, newUp, KeyEvent.KEYCODE_W);
                        updateKey(downActive != newDown, newDown, KeyEvent.KEYCODE_S);
                        updateKey(leftActive != newLeft, newLeft, KeyEvent.KEYCODE_A);
                        updateKey(rightActive != newRight, newRight, KeyEvent.KEYCODE_D);

                        upActive = newUp;
                        downActive = newDown;
                        leftActive = newLeft;
                        rightActive = newRight;
                    }
                    return true;

                case MotionEvent.ACTION_UP:
                case MotionEvent.ACTION_CANCEL:
                    releaseAll();
                    return true;
            }
            return false;
        }

        private void updateKey(boolean changed, boolean down, int keyCode) {
            if (changed) {
                if (down) vibrateTick();
                sendKey(down, keyCode);
            }
        }

        private void releaseAll() {
            if (upActive) sendKey(false, KeyEvent.KEYCODE_W);
            if (downActive) sendKey(false, KeyEvent.KEYCODE_S);
            if (leftActive) sendKey(false, KeyEvent.KEYCODE_A);
            if (rightActive) sendKey(false, KeyEvent.KEYCODE_D);
            upActive = downActive = leftActive = rightActive = false;
        }
    }

    // ==========================================
    // TRANSLUCENT JOYSTICK VIEW
    // ==========================================
    private class JoystickView extends View {
        private final ControlItem item;
        private final Paint basePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint stickPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint strokePaint = new Paint(Paint.ANTI_ALIAS_FLAG);

        private float stickX = 0f;
        private float stickY = 0f;

        public JoystickView(Context context, ControlItem item) {
            super(context);
            this.item = item;

            basePaint.setColor(COLOR_STEAM_BG);
            basePaint.setStyle(Paint.Style.FILL);

            strokePaint.setColor(COLOR_STEAM_BORDER);
            strokePaint.setStyle(Paint.Style.STROKE);
            strokePaint.setStrokeWidth(dpToPx(1.5f));

            stickPaint.setColor(0xBB00D2FF);
            stickPaint.setStyle(Paint.Style.FILL);
        }

        @Override
        protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            float cx = getWidth() / 2f;
            float cy = getHeight() / 2f;
            float radius = Math.min(cx, cy) - dpToPx(4);

            canvas.drawCircle(cx, cy, radius, basePaint);
            canvas.drawCircle(cx, cy, radius, strokePaint);

            float currentStickX = cx + stickX;
            float currentStickY = cy + stickY;
            canvas.drawCircle(currentStickX, currentStickY, radius * 0.4f, stickPaint);
        }

        @Override
        public boolean onTouchEvent(MotionEvent event) {
            if (isEditMode) return false;

            float cx = getWidth() / 2f;
            float cy = getHeight() / 2f;
            float maxDist = Math.min(cx, cy) * 0.6f;

            switch (event.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                case MotionEvent.ACTION_MOVE:
                    float dx = event.getX() - cx;
                    float dy = event.getY() - cy;
                    float dist = (float) Math.sqrt(dx * dx + dy * dy);
                    if (dist > maxDist) {
                        dx = (dx / dist) * maxDist;
                        dy = (dy / dist) * maxDist;
                    }
                    stickX = dx;
                    stickY = dy;
                    invalidate();

                    // Map stick to WASD
                    sendKey(stickY < -maxDist * 0.35f, KeyEvent.KEYCODE_W);
                    sendKey(stickY > maxDist * 0.35f, KeyEvent.KEYCODE_S);
                    sendKey(stickX < -maxDist * 0.35f, KeyEvent.KEYCODE_A);
                    sendKey(stickX > maxDist * 0.35f, KeyEvent.KEYCODE_D);
                    return true;

                case MotionEvent.ACTION_UP:
                case MotionEvent.ACTION_CANCEL:
                    stickX = 0f;
                    stickY = 0f;
                    invalidate();
                    sendKey(false, KeyEvent.KEYCODE_W);
                    sendKey(false, KeyEvent.KEYCODE_S);
                    sendKey(false, KeyEvent.KEYCODE_A);
                    sendKey(false, KeyEvent.KEYCODE_D);
                    return true;
            }
            return false;
        }
    }
}