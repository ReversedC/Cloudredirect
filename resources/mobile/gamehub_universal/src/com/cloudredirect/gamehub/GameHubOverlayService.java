package com.cloudredirect.gamehub;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.graphics.PixelFormat;
import android.os.Build;
import android.os.IBinder;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.widget.TextView;
import android.widget.Toast;

public class GameHubOverlayService extends Service {
    private static final String CHANNEL_ID = "gamehub_overlay_channel";
    private static final int NOTIF_ID = 9912;
    private static GameHubOverlayService instance;

    private WindowManager windowManager;
    private TextView floatingPill;
    private GameHubTouchView touchHudView;
    private WindowManager.LayoutParams pillParams;
    private WindowManager.LayoutParams hudParams;
    private boolean isHudExpanded = false;

    public static boolean isRunning() {
        return instance != null;
    }

    public static void stopService(Context context) {
        if (instance != null) {
            instance.stopSelf();
        } else {
            context.stopService(new Intent(context, GameHubOverlayService.class));
        }
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    @Override
    public void onCreate() {
        super.onCreate();
        instance = this;
        windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);

        startForeground(NOTIF_ID, createNotification());
        createFloatingPill();
    }

    private Notification createNotification() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel chan = new NotificationChannel(
                CHANNEL_ID,
                "GameHub Touch Controls",
                NotificationManager.IMPORTANCE_LOW
            );
            chan.setDescription("Keeps GameHub HUD overlay active during gameplay");
            NotificationManager nm = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
            if (nm != null) nm.createNotificationChannel(chan);
        }

        Intent launchIntent = new Intent(this, MainActivity.class);
        PendingIntent pi = PendingIntent.getActivity(
            this, 0, launchIntent,
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.M ? PendingIntent.FLAG_IMMUTABLE : 0
        );

        Notification.Builder builder;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            builder = new Notification.Builder(this, CHANNEL_ID);
        } else {
            builder = new Notification.Builder(this);
        }

        return builder
            .setContentTitle("GameHub Mobile HUD Active")
            .setContentText("Tap to open GameHub settings or drag floating HUD over games")
            .setSmallIcon(R.drawable.ic_launcher)
            .setContentIntent(pi)
            .setOngoing(true)
            .build();
    }

    private void createFloatingPill() {
        floatingPill = new TextView(this);
        floatingPill.setText("🎮 HUD");
        floatingPill.setTextSize(13);
        floatingPill.setTextColor(0xFF00D2FF);
        floatingPill.setTypeface(null, android.graphics.Typeface.BOLD);
        floatingPill.setBackgroundResource(R.drawable.steam_pill_bg);
        floatingPill.setPadding(28, 14, 28, 14);
        floatingPill.setGravity(Gravity.CENTER);

        int type = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
            ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
            : WindowManager.LayoutParams.TYPE_PHONE;

        pillParams = new WindowManager.LayoutParams(
            WindowManager.LayoutParams.WRAP_CONTENT,
            WindowManager.LayoutParams.WRAP_CONTENT,
            type,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
            PixelFormat.TRANSLUCENT
        );
        pillParams.gravity = Gravity.TOP | Gravity.START;
        pillParams.x = HudConfig.getPrefs(this).getInt(HudConfig.KEY_PILL_X, 30);
        pillParams.y = HudConfig.getPrefs(this).getInt(HudConfig.KEY_PILL_Y, 200);

        floatingPill.setOnTouchListener(new View.OnTouchListener() {
            private int initialX, initialY;
            private float initialTouchX, initialTouchY;
            private boolean isDrag = false;

            @Override
            public boolean onTouch(View v, MotionEvent event) {
                switch (event.getAction()) {
                    case MotionEvent.ACTION_DOWN:
                        initialX = pillParams.x;
                        initialY = pillParams.y;
                        initialTouchX = event.getRawX();
                        initialTouchY = event.getRawY();
                        isDrag = false;
                        return true;

                    case MotionEvent.ACTION_MOVE:
                        int dx = (int) (event.getRawX() - initialTouchX);
                        int dy = (int) (event.getRawY() - initialTouchY);
                        if (Math.abs(dx) > 10 || Math.abs(dy) > 10) isDrag = true;
                        pillParams.x = initialX + dx;
                        pillParams.y = initialY + dy;
                        windowManager.updateViewLayout(floatingPill, pillParams);
                        return true;

                    case MotionEvent.ACTION_UP:
                        if (!isDrag) {
                            expandHud();
                        } else {
                            HudConfig.getPrefs(GameHubOverlayService.this).edit()
                                .putInt(HudConfig.KEY_PILL_X, pillParams.x)
                                .putInt(HudConfig.KEY_PILL_Y, pillParams.y)
                                .apply();
                        }
                        return true;
                }
                return false;
            }
        });

        windowManager.addView(floatingPill, pillParams);
    }

    private void expandHud() {
        if (isHudExpanded) return;
        isHudExpanded = true;

        if (floatingPill != null) {
            floatingPill.setVisibility(View.GONE);
        }

        if (touchHudView == null) {
            touchHudView = new GameHubTouchView(this);
            touchHudView.setOnToolbarActionListener(new GameHubTouchView.OnToolbarActionListener() {
                @Override
                public void onCollapse() {
                    collapseHud();
                }

                @Override
                public void onOpacityCycle() {
                    int cur = HudConfig.getPrefs(GameHubOverlayService.this).getInt(HudConfig.KEY_OPACITY, 1);
                    cur = (cur + 1) % 4;
                    HudConfig.getPrefs(GameHubOverlayService.this).edit().putInt(HudConfig.KEY_OPACITY, cur).apply();
                    float newAlpha = HudConfig.getOpacity(GameHubOverlayService.this);
                    touchHudView.setHudAlpha(newAlpha);
                    Toast.makeText(GameHubOverlayService.this, "Opacity: " + (int)(newAlpha * 100) + "%", Toast.LENGTH_SHORT).show();
                }

                @Override
                public void onPresetCycle() {
                    String curPreset = HudConfig.getPrefs(GameHubOverlayService.this).getString(HudConfig.KEY_PRESET, HudConfig.PRESET_STEAM_LINK);
                    String nextPreset;
                    if (HudConfig.PRESET_STEAM_LINK.equals(curPreset)) {
                        nextPreset = HudConfig.PRESET_ACTION_RPG;
                    } else if (HudConfig.PRESET_ACTION_RPG.equals(curPreset)) {
                        nextPreset = HudConfig.PRESET_FPS;
                    } else {
                        nextPreset = HudConfig.PRESET_STEAM_LINK;
                    }
                    HudConfig.getPrefs(GameHubOverlayService.this).edit().putString(HudConfig.KEY_PRESET, nextPreset).apply();
                    HudConfig.resetControls(GameHubOverlayService.this);
                    touchHudView.reloadControls();
                    Toast.makeText(GameHubOverlayService.this, "Preset: " + nextPreset, Toast.LENGTH_SHORT).show();
                }

                @Override
                public void onSaveLayout() {
                    Toast.makeText(GameHubOverlayService.this, "HUD Layout Saved! 🎮", Toast.LENGTH_SHORT).show();
                }
            });

            int type = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
                : WindowManager.LayoutParams.TYPE_PHONE;

            hudParams = new WindowManager.LayoutParams(
                WindowManager.LayoutParams.MATCH_PARENT,
                WindowManager.LayoutParams.MATCH_PARENT,
                type,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
                PixelFormat.TRANSLUCENT
            );
            hudParams.gravity = Gravity.TOP | Gravity.START;

            windowManager.addView(touchHudView, hudParams);
        } else {
            touchHudView.setVisibility(View.VISIBLE);
        }
    }

    private void collapseHud() {
        if (!isHudExpanded) return;
        isHudExpanded = false;

        if (touchHudView != null) {
            touchHudView.setVisibility(View.GONE);
        }
        if (floatingPill != null) {
            floatingPill.setVisibility(View.VISIBLE);
        }
    }

    @Override
    public void onDestroy() {
        super.onDestroy();
        instance = null;
        if (floatingPill != null && floatingPill.isAttachedToWindow()) {
            windowManager.removeView(floatingPill);
        }
        if (touchHudView != null && touchHudView.isAttachedToWindow()) {
            windowManager.removeView(touchHudView);
        }
    }
}
