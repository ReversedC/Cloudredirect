package com.cloudredirect.gamehub;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.util.DisplayMetrics;
import android.view.MotionEvent;
import android.view.View;
import android.widget.Toast;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

public class GameHubTouchView extends View {
    private final Vibrator vibrator;
    private final List<HudConfig.ControlDef> controls = new ArrayList<HudConfig.ControlDef>();
    private boolean isEditMode = false;
    private float hudAlpha = 0.70f;
    private final GameHubInputSender inputSender;

    // Paints
    private final Paint basePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint knobPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint textPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint toolbarPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint editPaint = new Paint(Paint.ANTI_ALIAS_FLAG);

    // Multi-touch tracking: pointerId -> control
    private final Map<Integer, ActiveTouch> activeTouches = new HashMap<Integer, ActiveTouch>();

    public interface OnToolbarActionListener {
        void onCollapse();
        void onOpacityCycle();
        void onPresetCycle();
        void onSaveLayout();
    }
    private OnToolbarActionListener toolbarListener;

    public static class ActiveTouch {
        public HudConfig.ControlDef control;
        public float currentX;
        public float currentY;
        public float stickDeltaX;
        public float stickDeltaY;
        public String lastDpadDirection;

        public ActiveTouch(HudConfig.ControlDef control, float x, float y) {
            this.control = control;
            this.currentX = x;
            this.currentY = y;
        }
    }

    public GameHubTouchView(Context context) {
        super(context);
        vibrator = (Vibrator) context.getSystemService(Context.VIBRATOR_SERVICE);
        inputSender = GameHubInputSender.getInstance(context);
        hudAlpha = HudConfig.getOpacity(context);

        // Load controls from config
        controls.addAll(HudConfig.getControls(context));

        textPaint.setTextAlign(Paint.Align.CENTER);
        textPaint.setTypeface(Typeface.create(Typeface.DEFAULT, Typeface.BOLD));

        editPaint.setStyle(Paint.Style.STROKE);
        editPaint.setStrokeWidth(3f);
        editPaint.setColor(0xFF00D2FF);
    }

    public void setOnToolbarActionListener(OnToolbarActionListener listener) {
        this.toolbarListener = listener;
    }

    public void setHudAlpha(float alpha) {
        this.hudAlpha = alpha;
        invalidate();
    }

    public void reloadControls() {
        controls.clear();
        controls.addAll(HudConfig.getControls(getContext()));
        invalidate();
    }

    public boolean toggleEditMode() {
        isEditMode = !isEditMode;
        invalidate();
        return isEditMode;
    }

    public boolean isEditMode() {
        return isEditMode;
    }

    private void triggerHaptic() {
        if (!HudConfig.isHapticsEnabled(getContext()) || vibrator == null) return;
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                vibrator.vibrate(VibrationEffect.createOneShot(18, VibrationEffect.DEFAULT_AMPLITUDE));
            } else {
                vibrator.vibrate(18);
            }
        } catch (Exception ignored) {}
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        int w = getWidth();
        int h = getHeight();
        if (w <= 0 || h <= 0) return;

        // 1. Draw Toolbar at top
        drawTopToolbar(canvas, w, h);

        // 2. Draw each control
        for (HudConfig.ControlDef def : controls) {
            float cx = def.xRatio * w;
            float cy = def.yRatio * h;
            float sizePx = dpToPx(def.sizeDp);

            boolean isPressed = isControlPressed(def);

            if ("stick".equals(def.type)) {
                drawJoystick(canvas, def, cx, cy, sizePx);
            } else if ("dpad".equals(def.type)) {
                drawDpad(canvas, def, cx, cy, sizePx);
            } else {
                drawButton(canvas, def, cx, cy, sizePx, isPressed);
            }

            // In Edit Mode, draw cyan bounding box
            if (isEditMode) {
                canvas.drawRoundRect(cx - sizePx / 2 - 4, cy - sizePx / 2 - 4,
                        cx + sizePx / 2 + 4, cy + sizePx / 2 + 4, 12, 12, editPaint);
            }
        }
    }

    private void drawTopToolbar(Canvas canvas, int w, int h) {
        toolbarPaint.setStyle(Paint.Style.FILL);
        toolbarPaint.setColor(0xCC0E151E);
        canvas.drawRoundRect(w * 0.20f, 10, w * 0.80f, 65, 24, 24, toolbarPaint);

        toolbarPaint.setStyle(Paint.Style.STROKE);
        toolbarPaint.setStrokeWidth(2f);
        toolbarPaint.setColor(0xFF283A4E);
        canvas.drawRoundRect(w * 0.20f, 10, w * 0.80f, 65, 24, 24, toolbarPaint);

        // Buttons inside toolbar
        textPaint.setTextSize(spToPx(12));
        textPaint.setColor(isEditMode ? 0xFFA4D007 : 0xFF00D2FF);
        canvas.drawText(isEditMode ? "✎ EDIT (ON)" : "✎ EDIT", w * 0.28f, 44, textPaint);

        textPaint.setColor(0xFF66C0F4);
        int pct = (int)(hudAlpha * 100);
        canvas.drawText("👁 " + pct + "%", w * 0.41f, 44, textPaint);

        textPaint.setColor(0xFF66C0F4);
        canvas.drawText("⚙ PRESET", w * 0.54f, 44, textPaint);

        textPaint.setColor(0xFFE5A823);
        canvas.drawText("💾 SAVE", w * 0.66f, 44, textPaint);

        textPaint.setColor(0xFFD83B3B);
        canvas.drawText("✖ CLOSE", w * 0.75f, 44, textPaint);
    }

    private void drawJoystick(Canvas canvas, HudConfig.ControlDef def, float cx, float cy, float sizePx) {
        float r = sizePx / 2f;
        int alpha = (int)(hudAlpha * 255);

        // Base Ring
        basePaint.setStyle(Paint.Style.FILL);
        basePaint.setColor(Color.argb((int)(alpha * 0.35f), 16, 23, 34));
        canvas.drawCircle(cx, cy, r, basePaint);

        basePaint.setStyle(Paint.Style.STROKE);
        basePaint.setStrokeWidth(3f);
        basePaint.setColor(Color.argb((int)(alpha * 0.75f), 0, 210, 255));
        canvas.drawCircle(cx, cy, r, basePaint);

        // Label
        textPaint.setColor(Color.argb(alpha, 140, 160, 180));
        textPaint.setTextSize(spToPx(11));
        canvas.drawText(def.label, cx, cy - r * 0.35f, textPaint);

        // Dynamic Knob
        float knobX = cx;
        float knobY = cy;
        ActiveTouch touch = getTouchForControl(def);
        if (touch != null) {
            knobX = cx + touch.stickDeltaX;
            knobY = cy + touch.stickDeltaY;
        }

        float knobR = r * 0.42f;
        knobPaint.setStyle(Paint.Style.FILL);
        knobPaint.setColor(Color.argb(alpha, 25, 42, 60));
        canvas.drawCircle(knobX, knobY, knobR, knobPaint);

        knobPaint.setStyle(Paint.Style.STROKE);
        knobPaint.setStrokeWidth(2.5f);
        knobPaint.setColor(Color.argb(alpha, 0, 210, 255));
        canvas.drawCircle(knobX, knobY, knobR, knobPaint);
    }

    private void drawDpad(Canvas canvas, HudConfig.ControlDef def, float cx, float cy, float sizePx) {
        float r = sizePx / 2f;
        int alpha = (int)(hudAlpha * 255);

        basePaint.setStyle(Paint.Style.FILL);
        basePaint.setColor(Color.argb((int)(alpha * 0.40f), 16, 23, 34));

        // Cross shape
        float crossW = sizePx * 0.36f;
        canvas.drawRoundRect(cx - crossW / 2, cy - r, cx + crossW / 2, cy + r, 8, 8, basePaint);
        canvas.drawRoundRect(cx - r, cy - crossW / 2, cx + r, cy + crossW / 2, 8, 8, basePaint);

        basePaint.setStyle(Paint.Style.STROKE);
        basePaint.setStrokeWidth(2f);
        basePaint.setColor(Color.argb((int)(alpha * 0.65f), 102, 192, 244));
        canvas.drawRoundRect(cx - crossW / 2, cy - r, cx + crossW / 2, cy + r, 8, 8, basePaint);
        canvas.drawRoundRect(cx - r, cy - crossW / 2, cx + r, cy + crossW / 2, 8, 8, basePaint);

        // Arrows
        textPaint.setTextSize(spToPx(13));
        textPaint.setColor(Color.argb(alpha, 255, 255, 255));
        canvas.drawText("▲", cx, cy - r * 0.50f, textPaint);
        canvas.drawText("▼", cx, cy + r * 0.75f, textPaint);
        canvas.drawText("◀", cx - r * 0.60f, cy + spToPx(4), textPaint);
        canvas.drawText("▶", cx + r * 0.60f, cy + spToPx(4), textPaint);
    }

    private void drawButton(Canvas canvas, HudConfig.ControlDef def, float cx, float cy, float sizePx, boolean isPressed) {
        float r = sizePx / 2f;
        int alpha = (int)(hudAlpha * 255);

        int btnColor = def.color;
        int red = Color.red(btnColor);
        int green = Color.green(btnColor);
        int blue = Color.blue(btnColor);

        basePaint.setStyle(Paint.Style.FILL);
        if (isPressed) {
            basePaint.setColor(Color.argb((int)(alpha * 0.90f), red, green, blue));
        } else {
            basePaint.setColor(Color.argb((int)(alpha * 0.45f), red / 2, green / 2, blue / 2));
        }
        canvas.drawCircle(cx, cy, r, basePaint);

        basePaint.setStyle(Paint.Style.STROKE);
        basePaint.setStrokeWidth(2.5f);
        basePaint.setColor(Color.argb(alpha, red, green, blue));
        canvas.drawCircle(cx, cy, r, basePaint);

        // Label
        textPaint.setTextSize(spToPx(def.label.length() > 3 ? 10 : (def.label.length() > 2 ? 12 : 16)));
        textPaint.setColor(isPressed ? 0xFFFFFFFF : Color.argb(alpha, 245, 245, 245));
        canvas.drawText(def.label, cx, cy + spToPx(def.label.length() > 2 ? 4 : 5), textPaint);
    }

    @Override
    public boolean onTouchEvent(MotionEvent event) {
        int action = event.getActionMasked();
        int pointerIndex = event.getActionIndex();
        int pointerId = event.getPointerId(pointerIndex);
        float x = event.getX(pointerIndex);
        float y = event.getY(pointerIndex);
        int w = getWidth();
        int h = getHeight();

        switch (action) {
            case MotionEvent.ACTION_DOWN:
            case MotionEvent.ACTION_POINTER_DOWN:
                // Check Toolbar clicks first
                if (y >= 10 && y <= 65 && x >= w * 0.20f && x <= w * 0.80f) {
                    handleToolbarClick(x, w);
                    return true;
                }

                // Check controls
                HudConfig.ControlDef hit = findHitControl(x, y, w, h);
                if (hit != null) {
                    ActiveTouch touch = new ActiveTouch(hit, x, y);
                    activeTouches.put(pointerId, touch);
                    triggerHaptic();

                    if ("stick".equals(hit.type)) {
                        updateStickDelta(touch, hit, x, y, w, h);
                    } else if ("dpad".equals(hit.type)) {
                        updateDpadDirection(touch, hit, x, y, w, h);
                    } else {
                        // Action button down: send to PC and local accessibility
                        inputSender.sendButton(hit, true);
                        GameHubAccessibilityService.dispatchTap(x, y);
                    }

                    invalidate();
                    return true;
                }
                break;

            case MotionEvent.ACTION_MOVE:
                for (int i = 0; i < event.getPointerCount(); i++) {
                    int pId = event.getPointerId(i);
                    ActiveTouch t = activeTouches.get(pId);
                    if (t != null) {
                        float px = event.getX(i);
                        float py = event.getY(i);

                        if (isEditMode) {
                            // Drag position in edit mode
                            t.control.xRatio = Math.max(0.05f, Math.min(0.95f, px / w));
                            t.control.yRatio = Math.max(0.12f, Math.min(0.95f, py / h));
                        } else {
                            if ("stick".equals(t.control.type)) {
                                updateStickDelta(t, t.control, px, py, w, h);
                            } else if ("dpad".equals(t.control.type)) {
                                updateDpadDirection(t, t.control, px, py, w, h);
                            }
                        }
                    }
                }
                invalidate();
                return true;

            case MotionEvent.ACTION_UP:
            case MotionEvent.ACTION_POINTER_UP:
            case MotionEvent.ACTION_CANCEL:
                ActiveTouch releaseTouch = activeTouches.remove(pointerId);
                if (releaseTouch != null) {
                    if ("stick".equals(releaseTouch.control.type)) {
                        inputSender.sendJoystick(releaseTouch.control.id, 0f, 0f);
                    } else if ("dpad".equals(releaseTouch.control.type)) {
                        if (releaseTouch.lastDpadDirection != null) {
                            inputSender.sendDpadDirection(releaseTouch.lastDpadDirection, false);
                        }
                    } else {
                        inputSender.sendButton(releaseTouch.control, false);
                    }
                }
                invalidate();
                return true;
        }

        return super.onTouchEvent(event);
    }

    private void handleToolbarClick(float x, int w) {
        triggerHaptic();
        if (x < w * 0.35f) {
            toggleEditMode();
        } else if (x < w * 0.47f) {
            cycleOpacityInternal();
            if (toolbarListener != null) toolbarListener.onOpacityCycle();
        } else if (x < w * 0.60f) {
            cyclePresetInternal();
            if (toolbarListener != null) toolbarListener.onPresetCycle();
        } else if (x < w * 0.71f) {
            HudConfig.saveControls(getContext(), controls);
            Toast.makeText(getContext(), "Layout Saved!", Toast.LENGTH_SHORT).show();
            if (toolbarListener != null) toolbarListener.onSaveLayout();
        } else {
            if (toolbarListener != null) toolbarListener.onCollapse();
        }
    }

    private void cycleOpacityInternal() {
        int level = HudConfig.getPrefs(getContext()).getInt(HudConfig.KEY_OPACITY, 1);
        level = (level + 1) % 4;
        HudConfig.getPrefs(getContext()).edit().putInt(HudConfig.KEY_OPACITY, level).apply();
        setHudAlpha(HudConfig.getOpacity(getContext()));
    }

    private void cyclePresetInternal() {
        String cur = HudConfig.getPrefs(getContext()).getString(HudConfig.KEY_PRESET, HudConfig.PRESET_STEAM_LINK);
        String next;
        if (HudConfig.PRESET_STEAM_LINK.equals(cur)) {
            next = HudConfig.PRESET_ACTION_RPG;
        } else if (HudConfig.PRESET_ACTION_RPG.equals(cur)) {
            next = HudConfig.PRESET_FPS;
        } else {
            next = HudConfig.PRESET_STEAM_LINK;
        }
        HudConfig.getPrefs(getContext()).edit().putString(HudConfig.KEY_PRESET, next).apply();
        HudConfig.resetControls(getContext());
        reloadControls();
        Toast.makeText(getContext(), "Preset: " + next, Toast.LENGTH_SHORT).show();
    }

    private void updateStickDelta(ActiveTouch touch, HudConfig.ControlDef def, float px, float py, int w, int h) {
        float cx = def.xRatio * w;
        float cy = def.yRatio * h;
        float maxR = dpToPx(def.sizeDp) * 0.45f;
        float dx = px - cx;
        float dy = py - cy;
        float dist = (float) Math.sqrt(dx * dx + dy * dy);
        if (dist > maxR && dist > 0) {
            dx = (dx / dist) * maxR;
            dy = (dy / dist) * maxR;
        }
        touch.stickDeltaX = dx;
        touch.stickDeltaY = dy;

        float normX = dx / maxR;
        float normY = dy / maxR;
        inputSender.sendJoystick(def.id, normX, normY);
    }

    private void updateDpadDirection(ActiveTouch touch, HudConfig.ControlDef def, float px, float py, int w, int h) {
        float cx = def.xRatio * w;
        float cy = def.yRatio * h;
        float dx = px - cx;
        float dy = py - cy;
        String dir = null;

        if (Math.abs(dy) > Math.abs(dx)) {
            dir = dy < 0 ? "UP" : "DOWN";
        } else {
            dir = dx < 0 ? "LEFT" : "RIGHT";
        }

        if (dir != null && !dir.equals(touch.lastDpadDirection)) {
            if (touch.lastDpadDirection != null) {
                inputSender.sendDpadDirection(touch.lastDpadDirection, false);
            }
            touch.lastDpadDirection = dir;
            inputSender.sendDpadDirection(dir, true);
        }
    }

    private HudConfig.ControlDef findHitControl(float x, float y, int w, int h) {
        for (HudConfig.ControlDef def : controls) {
            float cx = def.xRatio * w;
            float cy = def.yRatio * h;
            float r = dpToPx(def.sizeDp) / 2f + 16;
            float dx = x - cx;
            float dy = y - cy;
            if (dx * dx + dy * dy <= r * r) {
                return def;
            }
        }
        return null;
    }

    private boolean isControlPressed(HudConfig.ControlDef def) {
        for (ActiveTouch t : activeTouches.values()) {
            if (t.control == def) return true;
        }
        return false;
    }

    private ActiveTouch getTouchForControl(HudConfig.ControlDef def) {
        for (ActiveTouch t : activeTouches.values()) {
            if (t.control == def) return t;
        }
        return null;
    }

    private float dpToPx(int dp) {
        DisplayMetrics metrics = getResources().getDisplayMetrics();
        return dp * metrics.density;
    }

    private float spToPx(int sp) {
        DisplayMetrics metrics = getResources().getDisplayMetrics();
        return sp * metrics.scaledDensity;
    }
}
