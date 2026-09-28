package com.cloudredirect.gamehub;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Typeface;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.util.DisplayMetrics;
import android.view.MotionEvent;
import android.view.View;

public class SinglePlayControlView extends View {
    private final HudConfig.ControlDef def;
    private final GameHubInputSender inputSender;
    private float opacity = 0.75f;
    private boolean isPressed = false;

    // Joysticks
    private float stickDeltaX = 0f;
    private float stickDeltaY = 0f;

    // D-Pad
    private String lastDpadDir = null;

    // Paints
    private final Paint basePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint glowPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint textPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint knobPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint tickPaint = new Paint(Paint.ANTI_ALIAS_FLAG);

    private final Vibrator vibrator;

    public SinglePlayControlView(Context context, HudConfig.ControlDef def, float opacity) {
        super(context);
        this.def = def;
        this.opacity = opacity;
        this.inputSender = GameHubInputSender.getInstance(context);
        this.vibrator = (Vibrator) context.getSystemService(Context.VIBRATOR_SERVICE);

        setClickable(true);
        setFocusable(false);
        initPaints();
    }

    private void initPaints() {
        glowPaint.setStyle(Paint.Style.STROKE);
        glowPaint.setStrokeCap(Paint.Cap.ROUND);

        textPaint.setTextAlign(Paint.Align.CENTER);
        textPaint.setTypeface(Typeface.create("sans-serif", Typeface.BOLD));

        tickPaint.setColor(0x5500D2FF);
        tickPaint.setStrokeWidth(2f);
    }

    public HudConfig.ControlDef getDef() {
        return def;
    }

    public void setOpacity(float op) {
        this.opacity = op;
        invalidate();
    }

    @Override
    protected void onMeasure(int widthMeasureSpec, int heightMeasureSpec) {
        int size = (int) dpToPx(def.sizeDp);
        setMeasuredDimension(size, size);
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        int w = getWidth();
        int h = getHeight();
        float cx = w / 2f;
        float cy = h / 2f;
        float size = Math.min(w, h);

        if ("stick".equals(def.type) || "stick_right".equals(def.type)) {
            drawJoystick(canvas, cx, cy, size);
        } else if ("dpad".equals(def.type) || "dpad_arrow".equals(def.type)) {
            drawDpad(canvas, cx, cy, size);
        } else {
            drawButton(canvas, cx, cy, size);
        }
    }

    private void drawButton(Canvas canvas, float cx, float cy, float sizePx) {
        float r = sizePx / 2f - dpToPx(2);
        int alpha = (int) (opacity * 255);
        String label = def.label;

        int neonColor = 0xFF00D2FF;
        boolean isAbxy = false;
        if ("A".equals(label)) { neonColor = 0xFF66D18F; isAbxy = true; }
        else if ("B".equals(label)) { neonColor = 0xFFF05454; isAbxy = true; }
        else if ("X".equals(label)) { neonColor = 0xFF4DA6FF; isAbxy = true; }
        else if ("Y".equals(label)) { neonColor = 0xFFFFC93C; isAbxy = true; }
        else if ("RT".equals(label) || "LT".equals(label) || "FIRE".equals(label)) { neonColor = 0xFFFF7B00; }

        boolean isRect = (label.length() > 3);

        if (isRect) {
            float rw = sizePx * 0.90f;
            float rh = sizePx * 0.58f;
            float left = cx - rw / 2f;
            float top = cy - rh / 2f;
            float right = cx + rw / 2f;
            float bottom = cy + rh / 2f;

            basePaint.setStyle(Paint.Style.FILL);
            basePaint.setColor(isPressed ? Color.argb((int)(alpha * 0.92f), 0, 210, 255)
                                        : Color.argb((int)(alpha * 0.50f), 14, 21, 30));
            canvas.drawRoundRect(left, top, right, bottom, 14, 14, basePaint);

            glowPaint.setStrokeWidth(isPressed ? 3f : 2f);
            glowPaint.setColor(isPressed ? 0xFFFFFFFF : Color.argb((int)(alpha * 0.85f), Color.red(neonColor), Color.green(neonColor), Color.blue(neonColor)));
            canvas.drawRoundRect(left, top, right, bottom, 14, 14, glowPaint);

            textPaint.setTextSize(spToPx(label.length() > 6 ? 9.5f : (label.length() > 4 ? 11f : 13f)));
            textPaint.setColor(isPressed ? 0xFFFFFFFF : Color.argb(alpha, 245, 245, 245));
            canvas.drawText(label, cx, cy + spToPx(4.5f), textPaint);
        } else {
            // Circle button
            basePaint.setStyle(Paint.Style.FILL);
            basePaint.setColor(isPressed ? Color.argb((int)(alpha * 0.92f), Color.red(neonColor), Color.green(neonColor), Color.blue(neonColor))
                                        : Color.argb((int)(alpha * 0.50f), 14, 21, 30));
            canvas.drawCircle(cx, cy, r, basePaint);

            glowPaint.setStrokeWidth(isPressed ? 3.5f : 2.2f);
            glowPaint.setColor(isPressed ? 0xFFFFFFFF : Color.argb((int)(alpha * 0.85f), Color.red(neonColor), Color.green(neonColor), Color.blue(neonColor)));
            canvas.drawCircle(cx, cy, r, glowPaint);

            // Inner dish ring
            glowPaint.setStrokeWidth(1.2f);
            glowPaint.setColor(Color.argb((int)(alpha * 0.35f), 255, 255, 255));
            canvas.drawCircle(cx, cy, r * 0.82f, glowPaint);

            float textSize = isAbxy ? spToPx(20) : spToPx(label.length() > 3 ? 10 : (label.length() > 1 ? 12 : 16));
            textPaint.setTextSize(textSize);
            textPaint.setColor(isPressed ? 0xFFFFFFFF : (isAbxy ? neonColor : Color.argb(alpha, 245, 245, 245)));
            canvas.drawText(label, cx, cy + spToPx(isAbxy ? 6.5f : 4.5f), textPaint);
        }
    }

    private void drawDpad(Canvas canvas, float cx, float cy, float sizePx) {
        float r = sizePx / 2f - dpToPx(2);
        int alpha = (int) (opacity * 255);
        boolean isWasd = !"dpad_arrow".equals(def.type);

        float armW = sizePx * 0.36f;
        float armL = r;

        // Dark Cross
        basePaint.setStyle(Paint.Style.FILL);
        basePaint.setColor(Color.argb((int)(alpha * 0.55f), 14, 21, 30));
        canvas.drawRoundRect(cx - armW / 2, cy - armL, cx + armW / 2, cy + armL, 12, 12, basePaint);
        canvas.drawRoundRect(cx - armL, cy - armW / 2, cx + armL, cy + armW / 2, 12, 12, basePaint);

        glowPaint.setStrokeWidth(2.2f);
        glowPaint.setColor(Color.argb((int)(alpha * 0.70f), 0, 210, 255));
        canvas.drawRoundRect(cx - armW / 2, cy - armL, cx + armW / 2, cy + armL, 12, 12, glowPaint);
        canvas.drawRoundRect(cx - armL, cy - armW / 2, cx + armL, cy + armW / 2, 12, 12, glowPaint);

        // Highlight active wing
        if (lastDpadDir != null) {
            basePaint.setColor(Color.argb((int)(alpha * 0.85f), 0, 210, 255));
            if ("UP".equals(lastDpadDir) || "ARROW_UP".equals(lastDpadDir)) {
                canvas.drawRoundRect(cx - armW / 2, cy - armL, cx + armW / 2, cy, 10, 10, basePaint);
            } else if ("DOWN".equals(lastDpadDir) || "ARROW_DOWN".equals(lastDpadDir)) {
                canvas.drawRoundRect(cx - armW / 2, cy, cx + armW / 2, cy + armL, 10, 10, basePaint);
            } else if ("LEFT".equals(lastDpadDir) || "ARROW_LEFT".equals(lastDpadDir)) {
                canvas.drawRoundRect(cx - armL, cy - armW / 2, cx, cy + armW / 2, 10, 10, basePaint);
            } else if ("RIGHT".equals(lastDpadDir) || "ARROW_RIGHT".equals(lastDpadDir)) {
                canvas.drawRoundRect(cx, cy - armW / 2, cx + armL, cy + armW / 2, 10, 10, basePaint);
            }
        }

        // Center Dish
        basePaint.setColor(Color.argb((int)(alpha * 0.80f), 10, 15, 22));
        canvas.drawCircle(cx, cy, armW * 0.58f, basePaint);
        glowPaint.setStrokeWidth(1.5f);
        glowPaint.setColor(Color.argb((int)(alpha * 0.40f), 0, 210, 255));
        canvas.drawCircle(cx, cy, armW * 0.58f, glowPaint);

        // Direction labels
        textPaint.setTextSize(spToPx(14));
        boolean upAct = "UP".equals(lastDpadDir) || "ARROW_UP".equals(lastDpadDir);
        textPaint.setColor(upAct ? 0xFFFFFFFF : Color.argb(alpha, 140, 190, 230));
        canvas.drawText(isWasd ? "W" : "▲", cx, cy - armL * 0.52f, textPaint);

        boolean downAct = "DOWN".equals(lastDpadDir) || "ARROW_DOWN".equals(lastDpadDir);
        textPaint.setColor(downAct ? 0xFFFFFFFF : Color.argb(alpha, 140, 190, 230));
        canvas.drawText(isWasd ? "S" : "▼", cx, cy + armL * 0.76f, textPaint);

        boolean leftAct = "LEFT".equals(lastDpadDir) || "ARROW_LEFT".equals(lastDpadDir);
        textPaint.setColor(leftAct ? 0xFFFFFFFF : Color.argb(alpha, 140, 190, 230));
        canvas.drawText(isWasd ? "A" : "◀", cx - armL * 0.62f, cy + spToPx(5), textPaint);

        boolean rightAct = "RIGHT".equals(lastDpadDir) || "ARROW_RIGHT".equals(lastDpadDir);
        textPaint.setColor(rightAct ? 0xFFFFFFFF : Color.argb(alpha, 140, 190, 230));
        canvas.drawText(isWasd ? "D" : "▶", cx + armL * 0.62f, cy + spToPx(5), textPaint);
    }

    private void drawJoystick(Canvas canvas, float cx, float cy, float sizePx) {
        float r = sizePx / 2f - dpToPx(3);
        int alpha = (int) (opacity * 255);

        // Outer Ring
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
            float x2 = cx + (float) Math.cos(rad) * (r + dpToPx(4));
            float y2 = cy + (float) Math.sin(rad) * (r + dpToPx(4));
            canvas.drawLine(x1, y1, x2, y2, tickPaint);
        }

        // Tag label
        textPaint.setColor(Color.argb(alpha, 120, 160, 200));
        textPaint.setTextSize(spToPx(10.5f));
        canvas.drawText(def.label, cx, cy - r * 0.40f, textPaint);

        // Knob Position
        float knobX = cx + stickDeltaX;
        float knobY = cy + stickDeltaY;

        if (isPressed) {
            glowPaint.setStrokeWidth(3f);
            glowPaint.setColor(Color.argb((int)(alpha * 0.65f), 0, 210, 255));
            canvas.drawLine(cx, cy, knobX, knobY, glowPaint);
        }

        // Thumbstick Knob
        float knobR = r * 0.38f;
        knobPaint.setStyle(Paint.Style.FILL);
        knobPaint.setColor(Color.argb(alpha, 25, 38, 52));
        canvas.drawCircle(knobX, knobY, knobR, knobPaint);

        knobPaint.setStyle(Paint.Style.STROKE);
        knobPaint.setStrokeWidth(2.5f);
        knobPaint.setColor(Color.argb(alpha, 0, 210, 255));
        canvas.drawCircle(knobX, knobY, knobR, knobPaint);

        knobPaint.setStyle(Paint.Style.FILL);
        knobPaint.setColor(Color.argb((int)(alpha * 0.90f), 15, 23, 32));
        canvas.drawCircle(knobX, knobY, knobR * 0.62f, knobPaint);

        glowPaint.setStrokeWidth(1.8f);
        glowPaint.setColor(Color.argb(alpha, 0, 210, 255));
        canvas.drawLine(knobX - dpToPx(4), knobY, knobX + dpToPx(4), knobY, glowPaint);
        canvas.drawLine(knobX, knobY - dpToPx(4), knobX, knobY + dpToPx(4), glowPaint);
    }

    @Override
    public boolean onTouchEvent(MotionEvent event) {
        float x = event.getX();
        float y = event.getY();
        int w = getWidth();
        int h = getHeight();
        float cx = w / 2f;
        float cy = h / 2f;

        switch (event.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                isPressed = true;
                triggerHaptic();

                if ("stick".equals(def.type) || "stick_right".equals(def.type)) {
                    updateStick(x, y, cx, cy);
                } else if ("dpad".equals(def.type) || "dpad_arrow".equals(def.type)) {
                    updateDpad(x, y, cx, cy);
                } else {
                    inputSender.sendButton(def, true);
                }
                invalidate();
                return true;

            case MotionEvent.ACTION_MOVE:
                if ("stick".equals(def.type) || "stick_right".equals(def.type)) {
                    updateStick(x, y, cx, cy);
                } else if ("dpad".equals(def.type) || "dpad_arrow".equals(def.type)) {
                    updateDpad(x, y, cx, cy);
                }
                invalidate();
                return true;

            case MotionEvent.ACTION_UP:
            case MotionEvent.ACTION_CANCEL:
                isPressed = false;
                if ("stick".equals(def.type) || "stick_right".equals(def.type)) {
                    stickDeltaX = 0f;
                    stickDeltaY = 0f;
                    inputSender.sendJoystick(def.id, 0f, 0f);
                } else if ("dpad".equals(def.type) || "dpad_arrow".equals(def.type)) {
                    if (lastDpadDir != null) {
                        inputSender.sendDpadDirection(lastDpadDir, false);
                        lastDpadDir = null;
                    }
                } else {
                    inputSender.sendButton(def, false);
                }
                invalidate();
                return true;
        }

        return super.onTouchEvent(event);
    }

    private void updateStick(float px, float py, float cx, float cy) {
        float maxR = (Math.min(getWidth(), getHeight()) / 2f) * 0.70f;
        float dx = px - cx;
        float dy = py - cy;
        float dist = (float) Math.hypot(dx, dy);
        if (dist > maxR && dist > 0) {
            dx = (dx / dist) * maxR;
            dy = (dy / dist) * maxR;
        }
        stickDeltaX = dx;
        stickDeltaY = dy;

        float normX = dx / maxR;
        float normY = dy / maxR;
        inputSender.sendJoystick(def.id, normX, normY);
    }

    private void updateDpad(float px, float py, float cx, float cy) {
        float dx = px - cx;
        float dy = py - cy;
        boolean isArrow = "dpad_arrow".equals(def.type);
        String dir;

        if (Math.abs(dy) > Math.abs(dx)) {
            dir = dy < 0 ? (isArrow ? "ARROW_UP" : "UP") : (isArrow ? "ARROW_DOWN" : "DOWN");
        } else {
            dir = dx < 0 ? (isArrow ? "ARROW_LEFT" : "LEFT") : (isArrow ? "ARROW_RIGHT" : "RIGHT");
        }

        if (dir != null && !dir.equals(lastDpadDir)) {
            if (lastDpadDir != null) {
                inputSender.sendDpadDirection(lastDpadDir, false);
            }
            lastDpadDir = dir;
            inputSender.sendDpadDirection(dir, true);
        }
    }

    private void triggerHaptic() {
        if (vibrator == null || !HudConfig.isHapticsEnabled(getContext())) return;
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                vibrator.vibrate(VibrationEffect.createOneShot(18, VibrationEffect.DEFAULT_AMPLITUDE));
            } else {
                vibrator.vibrate(18);
            }
        } catch (Throwable ignored) {}
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
