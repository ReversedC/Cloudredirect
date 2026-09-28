package com.cloudredirect.gamehub;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.DashPathEffect;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RadialGradient;
import android.graphics.RectF;
import android.graphics.Shader;
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
    private boolean isRemoveMode = false;
    private float hudAlpha = 0.75f;
    private final GameHubInputSender inputSender;

    // Selected control for resizing / editing
    private HudConfig.ControlDef selectedControl = null;
    private float lastPinchDist = 0f;

    public interface OnControlSelectedListener {
        void onControlSelected(HudConfig.ControlDef def);
    }
    private OnControlSelectedListener controlSelectedListener;

    // Paints
    private final Paint basePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint glowPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint knobPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint textPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint editPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint badgePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint tickPaint = new Paint(Paint.ANTI_ALIAS_FLAG);

    // Multi-touch tracking: pointerId -> ActiveTouch
    private final Map<Integer, ActiveTouch> activeTouches = new HashMap<Integer, ActiveTouch>();

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

        // Load configured controls
        controls.addAll(HudConfig.getControls(context));

        textPaint.setTextAlign(Paint.Align.CENTER);
        textPaint.setTypeface(Typeface.create("sans-serif-condensed", Typeface.BOLD));

        editPaint.setStyle(Paint.Style.STROKE);
        editPaint.setStrokeWidth(3f);
        editPaint.setColor(0xFF00D2FF);

        glowPaint.setStyle(Paint.Style.STROKE);

        badgePaint.setStyle(Paint.Style.FILL);

        tickPaint.setColor(0x8800D2FF);
        tickPaint.setStrokeWidth(2f);
        tickPaint.setStyle(Paint.Style.STROKE);
    }

    public void setOnControlSelectedListener(OnControlSelectedListener listener) {
        this.controlSelectedListener = listener;
    }

    public List<HudConfig.ControlDef> getControls() {
        return controls;
    }

    public HudConfig.ControlDef getSelectedControl() {
        return selectedControl;
    }

    public void setSelectedControl(HudConfig.ControlDef def) {
        this.selectedControl = def;
        invalidate();
        if (controlSelectedListener != null && def != null) {
            controlSelectedListener.onControlSelected(def);
        }
    }

    public void resizeSelectedControl(int deltaDp) {
        if (selectedControl == null) {
            if (!controls.isEmpty()) {
                selectedControl = controls.get(0);
            } else {
                return;
            }
        }

        boolean isStick = "stick".equals(selectedControl.type) || "stick_right".equals(selectedControl.type);
        boolean isDpad = "dpad".equals(selectedControl.type) || "dpad_arrow".equals(selectedControl.type);
        int min = (isStick || isDpad) ? 70 : 32;
        int max = (isStick || isDpad) ? 220 : 160;

        int newSize = Math.max(min, Math.min(max, selectedControl.sizeDp + deltaDp));
        if (newSize != selectedControl.sizeDp) {
            selectedControl.sizeDp = newSize;
            triggerHaptic();
            invalidate();
            if (controlSelectedListener != null) {
                controlSelectedListener.onControlSelected(selectedControl);
            }
        }
    }

    public void setSelectedControlSize(int sizeDp) {
        if (selectedControl == null) {
            if (!controls.isEmpty()) {
                selectedControl = controls.get(0);
            } else {
                return;
            }
        }

        boolean isStick = "stick".equals(selectedControl.type) || "stick_right".equals(selectedControl.type);
        boolean isDpad = "dpad".equals(selectedControl.type) || "dpad_arrow".equals(selectedControl.type);
        int min = (isStick || isDpad) ? 70 : 32;
        int max = (isStick || isDpad) ? 220 : 160;

        selectedControl.sizeDp = Math.max(min, Math.min(max, sizeDp));
        triggerHaptic();
        invalidate();
        if (controlSelectedListener != null) {
            controlSelectedListener.onControlSelected(selectedControl);
        }
    }

    public void addControl(HudConfig.ControlDef def) {
        controls.add(def);
        selectedControl = def;
        invalidate();
        if (controlSelectedListener != null) {
            controlSelectedListener.onControlSelected(def);
        }
    }

    public boolean removeControl(HudConfig.ControlDef def) {
        if (selectedControl == def) {
            selectedControl = null;
        }
        boolean removed = controls.remove(def);
        if (removed) invalidate();
        return removed;
    }

    public void setHudAlpha(float alpha) {
        this.hudAlpha = alpha;
        invalidate();
    }

    public float getHudAlpha() {
        return hudAlpha;
    }

    public void reloadControls() {
        controls.clear();
        controls.addAll(HudConfig.getControls(getContext()));
        selectedControl = null;
        invalidate();
    }

    public void saveControls() {
        HudConfig.saveControls(getContext(), controls);
    }

    public void setEditMode(boolean edit) {
        this.isEditMode = edit;
        if (edit) {
            this.isRemoveMode = false;
            if (selectedControl == null && !controls.isEmpty()) {
                selectedControl = controls.get(0);
                if (controlSelectedListener != null) {
                    controlSelectedListener.onControlSelected(selectedControl);
                }
            }
        } else {
            selectedControl = null;
        }
        invalidate();
    }

    public boolean isEditMode() {
        return isEditMode;
    }

    public void setRemoveMode(boolean remove) {
        this.isRemoveMode = remove;
        if (remove) {
            this.isEditMode = false;
            this.selectedControl = null;
        }
        invalidate();
    }

    public boolean isRemoveMode() {
        return isRemoveMode;
    }

    private void triggerHaptic() {
        if (!HudConfig.isHapticsEnabled(getContext()) || vibrator == null) return;
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                vibrator.vibrate(VibrationEffect.createOneShot(20, VibrationEffect.DEFAULT_AMPLITUDE));
            } else {
                vibrator.vibrate(20);
            }
        } catch (Exception ignored) {}
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        int w = getWidth();
        int h = getHeight();
        if (w <= 0 || h <= 0) return;

        // Render each controller component
        for (HudConfig.ControlDef def : controls) {
            float cx = def.xRatio * w;
            float cy = def.yRatio * h;
            float sizePx = dpToPx(def.sizeDp);
            boolean isPressed = isControlPressed(def);

            if ("stick".equals(def.type) || "stick_right".equals(def.type)) {
                drawJoystick(canvas, def, cx, cy, sizePx);
            } else if ("dpad".equals(def.type) || "dpad_arrow".equals(def.type)) {
                drawDpad(canvas, def, cx, cy, sizePx);
            } else {
                drawButton(canvas, def, cx, cy, sizePx, isPressed);
            }

            // Draw Edit / Move handles
            if (isEditMode) {
                drawEditFrame(canvas, def, cx, cy, sizePx);
            }

            // Draw Remove Badges
            if (isRemoveMode) {
                drawRemoveBadge(canvas, def, cx, cy, sizePx);
            }
        }
    }

    private void drawButton(Canvas canvas, HudConfig.ControlDef def, float cx, float cy, float sizePx, boolean isPressed) {
        int alpha = (int)(hudAlpha * 255);
        String label = def.label;
        boolean isAbxy = "A".equals(label) || "B".equals(label) || "X".equals(label) || "Y".equals(label);
        boolean isPill = "SPACE".equals(label) || "SHIFT".equals(label) || "LB".equals(label) || 
                         "RB".equals(label) || "LT".equals(label) || "RT".equals(label) || 
                         "CROUCH".equals(label) || "SPRINT".equals(label);

        float r = sizePx / 2f;
        if (isPressed) {
            r *= 0.94f; // Tactile physical depression simulation
        }

        // Determine button theme colors
        int neonColor = def.color;
        if (isAbxy) {
            if ("A".equals(label)) neonColor = 0xFF00E676; // Xbox/Steam Green
            else if ("B".equals(label)) neonColor = 0xFFFF1744; // Vivid Red
            else if ("X".equals(label)) neonColor = 0xFF00D2FF; // Cyber Cyan/Blue
            else if ("Y".equals(label)) neonColor = 0xFFFFD600; // Solar Yellow
        }

        int red = Color.red(neonColor);
        int green = Color.green(neonColor);
        int blue = Color.blue(neonColor);

        if (isPill) {
            // Pill geometry for wide buttons (Space, Shift, Bumpers)
            float pw = sizePx * 1.35f;
            float ph = sizePx * 0.72f;
            if (isPressed) { pw *= 0.95f; ph *= 0.95f; }
            RectF pillRect = new RectF(cx - pw / 2f, cy - ph / 2f, cx + pw / 2f, cy + ph / 2f);

            // Dark backfill
            basePaint.setStyle(Paint.Style.FILL);
            basePaint.setColor(isPressed ? Color.argb((int)(alpha * 0.85f), red, green, blue)
                                        : Color.argb((int)(alpha * 0.60f), 15, 23, 33));
            canvas.drawRoundRect(pillRect, 18, 18, basePaint);

            // Neon glowing border
            glowPaint.setStrokeWidth(isPressed ? 3.5f : 2.2f);
            glowPaint.setColor(Color.argb(alpha, red, green, blue));
            canvas.drawRoundRect(pillRect, 18, 18, glowPaint);

            // Text
            textPaint.setTextSize(spToPx(label.length() > 4 ? 11 : 13));
            textPaint.setColor(isPressed ? 0xFFFFFFFF : Color.argb(alpha, 240, 245, 250));
            canvas.drawText(label, cx, cy + spToPx(4), textPaint);
        } else {
            // Circular 3D button
            // 1. Drop shadow / outer bevel ring
            basePaint.setStyle(Paint.Style.FILL);
            basePaint.setColor(Color.argb((int)(alpha * 0.40f), 0, 0, 0));
            canvas.drawCircle(cx, cy + 2.5f, r + 2f, basePaint);

            // 2. Button Body (Dark carbon/slate)
            basePaint.setColor(isPressed ? Color.argb((int)(alpha * 0.88f), red, green, blue)
                                        : Color.argb((int)(alpha * 0.65f), 18, 27, 38));
            canvas.drawCircle(cx, cy, r, basePaint);

            // 3. Glowing Outer Rim
            glowPaint.setStrokeWidth(isPressed ? 3.8f : 2.5f);
            glowPaint.setColor(Color.argb(alpha, red, green, blue));
            canvas.drawCircle(cx, cy, r, glowPaint);

            // 4. Subtle inner concentric bevel
            glowPaint.setStrokeWidth(1.2f);
            glowPaint.setColor(Color.argb((int)(alpha * 0.35f), 255, 255, 255));
            canvas.drawCircle(cx, cy, r * 0.82f, glowPaint);

            // 5. Lettering
            float textSize = isAbxy ? spToPx(20) : spToPx(label.length() > 3 ? 10 : (label.length() > 1 ? 12 : 16));
            textPaint.setTextSize(textSize);
            textPaint.setColor(isPressed ? 0xFFFFFFFF : (isAbxy ? neonColor : Color.argb(alpha, 245, 245, 245)));
            canvas.drawText(label, cx, cy + spToPx(isAbxy ? 6.5f : 4.5f), textPaint);
        }
    }

    private void drawDpad(Canvas canvas, HudConfig.ControlDef def, float cx, float cy, float sizePx) {
        float r = sizePx / 2f;
        int alpha = (int)(hudAlpha * 255);
        boolean isWasd = !"dpad_arrow".equals(def.type);

        ActiveTouch touch = getTouchForControl(def);
        String activeDir = (touch != null) ? touch.lastDpadDirection : null;

        float armW = sizePx * 0.36f;
        float armL = r;

        // 1. Dark Cross Body
        basePaint.setStyle(Paint.Style.FILL);
        basePaint.setColor(Color.argb((int)(alpha * 0.55f), 14, 21, 30));
        canvas.drawRoundRect(cx - armW / 2, cy - armL, cx + armW / 2, cy + armL, 12, 12, basePaint);
        canvas.drawRoundRect(cx - armL, cy - armW / 2, cx + armL, cy + armW / 2, 12, 12, basePaint);

        // 2. Outline
        glowPaint.setStrokeWidth(2.2f);
        glowPaint.setColor(Color.argb((int)(alpha * 0.70f), 0, 210, 255));
        canvas.drawRoundRect(cx - armW / 2, cy - armL, cx + armW / 2, cy + armL, 12, 12, glowPaint);
        canvas.drawRoundRect(cx - armL, cy - armW / 2, cx + armL, cy + armW / 2, 12, 12, glowPaint);

        // 3. Highlight active directional wing if pressed
        if (activeDir != null) {
            basePaint.setColor(Color.argb((int)(alpha * 0.85f), 0, 210, 255));
            if ("UP".equals(activeDir) || "ARROW_UP".equals(activeDir)) {
                canvas.drawRoundRect(cx - armW / 2, cy - armL, cx + armW / 2, cy, 10, 10, basePaint);
            } else if ("DOWN".equals(activeDir) || "ARROW_DOWN".equals(activeDir)) {
                canvas.drawRoundRect(cx - armW / 2, cy, cx + armW / 2, cy + armL, 10, 10, basePaint);
            } else if ("LEFT".equals(activeDir) || "ARROW_LEFT".equals(activeDir)) {
                canvas.drawRoundRect(cx - armL, cy - armW / 2, cx, cy + armW / 2, 10, 10, basePaint);
            } else if ("RIGHT".equals(activeDir) || "ARROW_RIGHT".equals(activeDir)) {
                canvas.drawRoundRect(cx, cy - armW / 2, cx + armL, cy + armW / 2, 10, 10, basePaint);
            }
        }

        // 4. Center Thumb Rest Dish
        basePaint.setColor(Color.argb((int)(alpha * 0.80f), 10, 15, 22));
        canvas.drawCircle(cx, cy, armW * 0.58f, basePaint);
        glowPaint.setStrokeWidth(1.5f);
        glowPaint.setColor(Color.argb((int)(alpha * 0.40f), 0, 210, 255));
        canvas.drawCircle(cx, cy, armW * 0.58f, glowPaint);

        // 5. Engraved Wing Labels (WASD or Arrows)
        textPaint.setTextSize(spToPx(14));
        // UP
        boolean upAct = "UP".equals(activeDir) || "ARROW_UP".equals(activeDir);
        textPaint.setColor(upAct ? 0xFFFFFFFF : Color.argb(alpha, 140, 190, 230));
        canvas.drawText(isWasd ? "W" : "▲", cx, cy - armL * 0.52f, textPaint);

        // DOWN
        boolean downAct = "DOWN".equals(activeDir) || "ARROW_DOWN".equals(activeDir);
        textPaint.setColor(downAct ? 0xFFFFFFFF : Color.argb(alpha, 140, 190, 230));
        canvas.drawText(isWasd ? "S" : "▼", cx, cy + armL * 0.76f, textPaint);

        // LEFT
        boolean leftAct = "LEFT".equals(activeDir) || "ARROW_LEFT".equals(activeDir);
        textPaint.setColor(leftAct ? 0xFFFFFFFF : Color.argb(alpha, 140, 190, 230));
        canvas.drawText(isWasd ? "A" : "◀", cx - armL * 0.62f, cy + spToPx(5), textPaint);

        // RIGHT
        boolean rightAct = "RIGHT".equals(activeDir) || "ARROW_RIGHT".equals(activeDir);
        textPaint.setColor(rightAct ? 0xFFFFFFFF : Color.argb(alpha, 140, 190, 230));
        canvas.drawText(isWasd ? "D" : "▶", cx + armL * 0.62f, cy + spToPx(5), textPaint);
    }

    private void drawJoystick(Canvas canvas, HudConfig.ControlDef def, float cx, float cy, float sizePx) {
        float r = sizePx / 2f;
        int alpha = (int)(hudAlpha * 255);

        // 1. Outer Base Ring & Radial Graduation Ticks (45 degree intervals)
        basePaint.setStyle(Paint.Style.FILL);
        basePaint.setColor(Color.argb((int)(alpha * 0.35f), 12, 18, 26));
        canvas.drawCircle(cx, cy, r, basePaint);

        glowPaint.setStrokeWidth(2.6f);
        glowPaint.setColor(Color.argb((int)(alpha * 0.75f), 0, 210, 255));
        canvas.drawCircle(cx, cy, r, glowPaint);

        // Concentric inner deadzone ring
        glowPaint.setStrokeWidth(1.2f);
        glowPaint.setColor(Color.argb((int)(alpha * 0.30f), 0, 210, 255));
        canvas.drawCircle(cx, cy, r * 0.50f, glowPaint);

        // 8 Radial Ticks
        for (int deg = 0; deg < 360; deg += 45) {
            double rad = Math.toRadians(deg);
            float x1 = cx + (float) Math.cos(rad) * (r - dpToPx(3));
            float y1 = cy + (float) Math.sin(rad) * (r - dpToPx(3));
            float x2 = cx + (float) Math.cos(rad) * (r + dpToPx(5));
            float y2 = cy + (float) Math.sin(rad) * (r + dpToPx(5));
            canvas.drawLine(x1, y1, x2, y2, tickPaint);
        }

        // Top Header Tag
        textPaint.setColor(Color.argb(alpha, 120, 160, 200));
        textPaint.setTextSize(spToPx(10.5f));
        canvas.drawText(def.label, cx, cy - r * 0.40f, textPaint);

        // 2. Active Touch Knob Position
        float knobX = cx;
        float knobY = cy;
        ActiveTouch touch = getTouchForControl(def);
        if (touch != null) {
            knobX = cx + touch.stickDeltaX;
            knobY = cy + touch.stickDeltaY;

            // Draw glowing cyan beam connecting base center to knob
            glowPaint.setStrokeWidth(3f);
            glowPaint.setColor(Color.argb((int)(alpha * 0.65f), 0, 210, 255));
            canvas.drawLine(cx, cy, knobX, knobY, glowPaint);
        }

        // 3. Thumbstick Knob (Rubberized concave cap)
        float knobR = r * 0.38f;
        knobPaint.setStyle(Paint.Style.FILL);
        knobPaint.setColor(Color.argb(alpha, 25, 38, 52));
        canvas.drawCircle(knobX, knobY, knobR, knobPaint);

        // Outer knob rim
        knobPaint.setStyle(Paint.Style.STROKE);
        knobPaint.setStrokeWidth(2.5f);
        knobPaint.setColor(Color.argb(alpha, 0, 210, 255));
        canvas.drawCircle(knobX, knobY, knobR, knobPaint);

        // Inner concave thumb dish with textured grip
        knobPaint.setStyle(Paint.Style.FILL);
        knobPaint.setColor(Color.argb((int)(alpha * 0.90f), 15, 23, 32));
        canvas.drawCircle(knobX, knobY, knobR * 0.62f, knobPaint);

        // Center micro-crosshair (+)
        glowPaint.setStrokeWidth(1.8f);
        glowPaint.setColor(Color.argb(alpha, 0, 210, 255));
        canvas.drawLine(knobX - dpToPx(4), knobY, knobX + dpToPx(4), knobY, glowPaint);
        canvas.drawLine(knobX, knobY - dpToPx(4), knobX, knobY + dpToPx(4), glowPaint);
    }

    private void drawEditFrame(Canvas canvas, HudConfig.ControlDef def, float cx, float cy, float sizePx) {
        float half = sizePx / 2f + dpToPx(6);
        RectF bounds = new RectF(cx - half, cy - half, cx + half, cy + half);

        boolean isSelected = (def == selectedControl);

        // Highlight frame: selected is glowing amber/gold, others cyan
        editPaint.setColor(isSelected ? 0xFFFFD600 : 0xFF00D2FF);
        editPaint.setStrokeWidth(isSelected ? 3.5f : 2.0f);
        canvas.drawRoundRect(bounds, 12, 12, editPaint);

        if (isSelected) {
            // Draw prominent corner grip dots
            basePaint.setStyle(Paint.Style.FILL);
            basePaint.setColor(0xFFFFD600);
            canvas.drawCircle(bounds.left, bounds.top, dpToPx(4), basePaint);
            canvas.drawCircle(bounds.right, bounds.top, dpToPx(4), basePaint);
            canvas.drawCircle(bounds.left, bounds.bottom, dpToPx(4), basePaint);
            canvas.drawCircle(bounds.right, bounds.bottom, dpToPx(4), basePaint);
        }

        // Live Size indicator badge
        textPaint.setTextSize(spToPx(10f));
        textPaint.setColor(isSelected ? 0xFFFFD600 : 0xFF00D2FF);
        canvas.drawText("📐 " + def.sizeDp + "dp", cx, cy + half + dpToPx(13), textPaint);
    }

    private void drawRemoveBadge(Canvas canvas, HudConfig.ControlDef def, float cx, float cy, float sizePx) {
        float bx = cx + sizePx * 0.38f;
        float by = cy - sizePx * 0.38f;
        float badgeR = dpToPx(13);

        // Red circular badge
        badgePaint.setColor(0xFFE53935);
        canvas.drawCircle(bx, by, badgeR, badgePaint);

        // White border
        glowPaint.setStrokeWidth(1.8f);
        glowPaint.setColor(0xFFFFFFFF);
        canvas.drawCircle(bx, by, badgeR, glowPaint);

        // White 'X'
        textPaint.setTextSize(spToPx(13));
        textPaint.setColor(0xFFFFFFFF);
        canvas.drawText("✕", bx, by + spToPx(4.5f), textPaint);
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

        // Multi-touch Pinch to Resize in Edit Mode
        if (isEditMode && event.getPointerCount() >= 2) {
            float p0x = event.getX(0);
            float p0y = event.getY(0);
            float p1x = event.getX(1);
            float p1y = event.getY(1);
            float currentPinchDist = (float) Math.hypot(p0x - p1x, p0y - p1y);

            if (action == MotionEvent.ACTION_MOVE && lastPinchDist > 0) {
                float diff = (currentPinchDist - lastPinchDist) / dpToPx(6);
                if (Math.abs(diff) >= 1f) {
                    resizeSelectedControl((int) diff);
                    lastPinchDist = currentPinchDist;
                }
            } else {
                lastPinchDist = currentPinchDist;
            }
            return true;
        }

        switch (action) {
            case MotionEvent.ACTION_DOWN:
            case MotionEvent.ACTION_POINTER_DOWN:
                lastPinchDist = 0f;
                HudConfig.ControlDef hit = findHitControl(x, y, w, h);
                if (hit != null) {
                    if (isRemoveMode) {
                        controls.remove(hit);
                        if (selectedControl == hit) selectedControl = null;
                        triggerHaptic();
                        Toast.makeText(getContext(), "Removed: " + hit.label, Toast.LENGTH_SHORT).show();
                        invalidate();
                        return true;
                    }

                    if (isEditMode) {
                        setSelectedControl(hit);
                    }

                    ActiveTouch touch = new ActiveTouch(hit, x, y);
                    activeTouches.put(pointerId, touch);
                    triggerHaptic();

                    if (!isEditMode) {
                        if ("stick".equals(hit.type) || "stick_right".equals(hit.type)) {
                            updateStickDelta(touch, hit, x, y, w, h);
                        } else if ("dpad".equals(hit.type) || "dpad_arrow".equals(hit.type)) {
                            updateDpadDirection(touch, hit, x, y, w, h);
                        } else {
                            inputSender.sendButton(hit, true);
                            GameHubAccessibilityService.dispatchTap(x, y);
                        }
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
                            // Free drag and reposition
                            t.control.xRatio = Math.max(0.04f, Math.min(0.96f, px / w));
                            t.control.yRatio = Math.max(0.08f, Math.min(0.96f, py / h));
                        } else {
                            if ("stick".equals(t.control.type) || "stick_right".equals(t.control.type)) {
                                updateStickDelta(t, t.control, px, py, w, h);
                            } else if ("dpad".equals(t.control.type) || "dpad_arrow".equals(t.control.type)) {
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
                lastPinchDist = 0f;
                ActiveTouch releaseTouch = activeTouches.remove(pointerId);
                if (releaseTouch != null && !isEditMode && !isRemoveMode) {
                    if ("stick".equals(releaseTouch.control.type) || "stick_right".equals(releaseTouch.control.type)) {
                        inputSender.sendJoystick(releaseTouch.control.id, 0f, 0f);
                    } else if ("dpad".equals(releaseTouch.control.type) || "dpad_arrow".equals(releaseTouch.control.type)) {
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
        boolean isArrow = "dpad_arrow".equals(def.type);
        String dir = null;

        if (Math.abs(dy) > Math.abs(dx)) {
            dir = dy < 0 ? (isArrow ? "ARROW_UP" : "UP") : (isArrow ? "ARROW_DOWN" : "DOWN");
        } else {
            dir = dx < 0 ? (isArrow ? "ARROW_LEFT" : "LEFT") : (isArrow ? "ARROW_RIGHT" : "RIGHT");
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
            float r = dpToPx(def.sizeDp) / 2f + dpToPx(16);
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

    private float spToPx(float sp) {
        DisplayMetrics metrics = getResources().getDisplayMetrics();
        return sp * metrics.scaledDensity;
    }
}
