package com.cloudredirect.gamehub;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ActivityInfo;
import android.content.res.Configuration;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.IBinder;
import android.util.DisplayMetrics;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.view.inputmethod.InputMethodManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

import java.util.ArrayList;
import java.util.List;
import java.util.Random;

public class GameHubOverlayService extends Service {
    private static final String CHANNEL_ID = "gamehub_overlay_channel";
    private static final int NOTIF_ID = 9912;
    private static GameHubOverlayService instance;

    private WindowManager windowManager;

    // 1. Initial Floating Pill Badge
    private TextView floatingPill;
    private WindowManager.LayoutParams pillParams;

    // 2. Play Mode: Discrete, individual views allowing 100% native touch pass-through to Steam Link
    private final List<SinglePlayControlView> activePlayViews = new ArrayList<>();
    private LinearLayout miniPlayDock;
    private WindowManager.LayoutParams miniPlayDockParams;

    // 3. Edit Mode: Fullscreen interactive layout for drag, resize, add, remove
    private FrameLayout hudRootLayout;
    private WindowManager.LayoutParams hudParams;
    private GameHubTouchView touchHudView;

    // Edit Dock & Resize Bar
    private HorizontalScrollView dockScroll;
    private LinearLayout dockBar;
    private HorizontalScrollView sizeScroll;
    private LinearLayout sizeBar;
    private TextView tvSizeInfo;
    private Button btnSizeMinus;
    private Button btnSizePlus;

    // Add Control Overlay
    private FrameLayout addControlOverlay;
    private LinearLayout addItemsContainer;

    // Dock Buttons
    private Button btnDockSavePlay;
    private Button btnDockDone;
    private Button btnDockKeyboard;
    private Button btnDockAdd;
    private Button btnDockRemove;
    private Button btnDockMove;
    private Button btnDockOrientation;
    private Button btnDockOpacity;
    private Button btnDockPreset;
    private Button btnDockHideAll;

    // 4. In-Game Keyboard Modal
    private View keyboardModalView;
    private WindowManager.LayoutParams keyboardModalParams;

    private boolean isHudExpanded = false;
    private boolean isEditModeActive = false;
    private final Random random = new Random();

    public static boolean isRunning() {
        return instance != null;
    }

    public static GameHubOverlayService getInstance() {
        return instance;
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
        applyOrientationMode();
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
            .setContentText("Universal touch controller running. Tap to open dashboard.")
            .setSmallIcon(R.drawable.ic_launcher)
            .setContentIntent(pi)
            .setOngoing(true)
            .build();
    }

    // ==========================================
    // FLOATING PILL (Initial Compact Badge)
    // ==========================================
    private void createFloatingPill() {
        floatingPill = new TextView(this);
        floatingPill.setText("🎮 HUD");
        floatingPill.setTextSize(13);
        floatingPill.setTextColor(0xFF00D2FF);
        floatingPill.setTypeface(null, Typeface.BOLD);
        floatingPill.setBackgroundResource(R.drawable.steam_pill_bg);
        floatingPill.setPadding(dpToPx(14), dpToPx(8), dpToPx(14), dpToPx(8));
        floatingPill.setGravity(Gravity.CENTER);
        floatingPill.setAlpha(HudConfig.getOpacity(this));

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
                        try {
                            windowManager.updateViewLayout(floatingPill, pillParams);
                        } catch (Throwable ignored) {}
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

    // ==========================================
    // EXPAND & COLLAPSE HUD
    // ==========================================
    private void expandHud() {
        if (isHudExpanded) return;
        isHudExpanded = true;

        if (floatingPill != null) {
            floatingPill.setVisibility(View.GONE);
        }

        // Start directly in Play Mode with discrete floating controls
        spawnPlayModeViews();
    }

    private void collapseHud() {
        isHudExpanded = false;
        isEditModeActive = false;

        clearPlayModeViews();
        hideKeyboardModal();

        if (hudRootLayout != null) {
            hudRootLayout.setVisibility(View.GONE);
        }
        if (floatingPill != null) {
            floatingPill.setVisibility(View.VISIBLE);
        }
    }

    // =========================================================================
    // PLAY MODE: DISCRETE FLOATING CONTROLS (SOLVES TOUCH BLOCKING COMPLETELY!)
    // =========================================================================
    private void spawnPlayModeViews() {
        clearPlayModeViews();
        isEditModeActive = false;

        if (hudRootLayout != null) {
            hudRootLayout.setVisibility(View.GONE);
        }

        float opacity = HudConfig.getOpacity(this);
        List<HudConfig.ControlDef> controls = HudConfig.getControls(this);
        DisplayMetrics dm = getResources().getDisplayMetrics();
        int screenW = dm.widthPixels;
        int screenH = dm.heightPixels;

        int windowType = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
            ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
            : WindowManager.LayoutParams.TYPE_PHONE;

        // 1. Spawn each button/dpad/joystick as its own separate tiny floating window
        for (HudConfig.ControlDef def : controls) {
            SinglePlayControlView ctrlView = new SinglePlayControlView(this, def, opacity);
            int sizePx = (int) (def.sizeDp * dm.density);
            int cx = (int) (def.xRatio * screenW);
            int cy = (int) (def.yRatio * screenH);
            int x = cx - sizePx / 2;
            int y = cy - sizePx / 2;

            WindowManager.LayoutParams params = new WindowManager.LayoutParams(
                sizePx, sizePx,
                windowType,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
                PixelFormat.TRANSLUCENT
            );
            params.gravity = Gravity.TOP | Gravity.START;
            params.x = x;
            params.y = y;

            try {
                windowManager.addView(ctrlView, params);
                activePlayViews.add(ctrlView);
            } catch (Exception e) {
                e.printStackTrace();
            }
        }

        // 2. Spawn compact Mini Play Dock at top: [ ⌨ KEYBOARD ] [ ⚙ EDIT HUD ] [ ✕ ]
        createMiniPlayDock(windowType);
    }

    private void clearPlayModeViews() {
        for (SinglePlayControlView v : activePlayViews) {
            try {
                windowManager.removeView(v);
            } catch (Exception ignored) {}
        }
        activePlayViews.clear();

        if (miniPlayDock != null) {
            try {
                windowManager.removeView(miniPlayDock);
            } catch (Exception ignored) {}
            miniPlayDock = null;
        }
    }

    private void createMiniPlayDock(int windowType) {
        if (miniPlayDock != null) {
            try { windowManager.removeView(miniPlayDock); } catch (Exception ignored) {}
        }

        float opacity = HudConfig.getOpacity(this);

        miniPlayDock = new LinearLayout(this);
        miniPlayDock.setOrientation(LinearLayout.HORIZONTAL);
        miniPlayDock.setGravity(Gravity.CENTER_VERTICAL);
        miniPlayDock.setPadding(dpToPx(8), dpToPx(4), dpToPx(8), dpToPx(4));
        applyRoundedCardBg(miniPlayDock, 0xD90B131D, 0xAA00D2FF, dpToPx(16));
        miniPlayDock.setAlpha(opacity);

        // [ ⌨ KEYBOARD ] Button
        TextView btnKb = new TextView(this);
        btnKb.setText("⌨ KEYBOARD");
        btnKb.setTextSize(11f);
        btnKb.setTextColor(0xFFFFFFFF);
        btnKb.setTypeface(null, Typeface.BOLD);
        btnKb.setPadding(dpToPx(10), dpToPx(5), dpToPx(10), dpToPx(5));
        applyRoundedCardBg(btnKb, 0xEE1E4466, 0xFF00D2FF, dpToPx(12));
        btnKb.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                showKeyboardModal();
            }
        });
        miniPlayDock.addView(btnKb);

        // [ ⚙ EDIT HUD ] Button
        TextView btnEdit = new TextView(this);
        btnEdit.setText("⚙ EDIT HUD");
        btnEdit.setTextSize(11f);
        btnEdit.setTextColor(0xFF00D2FF);
        btnEdit.setTypeface(null, Typeface.BOLD);
        btnEdit.setPadding(dpToPx(10), dpToPx(5), dpToPx(10), dpToPx(5));
        LinearLayout.LayoutParams editLp = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        editLp.leftMargin = dpToPx(6);
        btnEdit.setLayoutParams(editLp);
        applyRoundedCardBg(btnEdit, 0xAA0E1824, 0x8800D2FF, dpToPx(12));
        btnEdit.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                enterEditMode();
            }
        });
        miniPlayDock.addView(btnEdit);

        // [ ✕ ] Hide Button
        TextView btnClose = new TextView(this);
        btnClose.setText("✕");
        btnClose.setTextSize(12f);
        btnClose.setTextColor(0xFF9EABB8);
        btnClose.setTypeface(null, Typeface.BOLD);
        btnClose.setPadding(dpToPx(8), dpToPx(5), dpToPx(8), dpToPx(5));
        LinearLayout.LayoutParams closeLp = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        closeLp.leftMargin = dpToPx(6);
        btnClose.setLayoutParams(closeLp);
        btnClose.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                collapseHud();
            }
        });
        miniPlayDock.addView(btnClose);

        miniPlayDockParams = new WindowManager.LayoutParams(
            WindowManager.LayoutParams.WRAP_CONTENT,
            WindowManager.LayoutParams.WRAP_CONTENT,
            windowType,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
            PixelFormat.TRANSLUCENT
        );
        miniPlayDockParams.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
        miniPlayDockParams.y = dpToPx(8);

        windowManager.addView(miniPlayDock, miniPlayDockParams);
    }

    // ==========================================
    // EDIT MODE: FULLSCREEN INTERACTIVE CANVAS
    // ==========================================
    private void enterEditMode() {
        isEditModeActive = true;

        // Remove discrete play views so full drag-and-drop canvas has complete control
        clearPlayModeViews();

        if (hudRootLayout == null) {
            buildHudViewHierarchy();

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
            windowManager.addView(hudRootLayout, hudParams);
        } else {
            hudRootLayout.setVisibility(View.VISIBLE);
        }

        if (dockScroll != null) dockScroll.setVisibility(View.VISIBLE);
        if (touchHudView != null) {
            touchHudView.reloadControls();
            touchHudView.setEditMode(true);
            touchHudView.setRemoveMode(false);
        }
        if (sizeScroll != null) {
            sizeScroll.setVisibility(View.VISIBLE);
            updateSelectedControlSizeLabel();
        }
        updateDockButtonStates();
        Toast.makeText(this, "Edit Mode: Drag to move, use bottom bar to RESIZE! Tap [SAVE & PLAY] to lock.", Toast.LENGTH_SHORT).show();
    }

    private void exitEditModeToPlay() {
        if (touchHudView != null) {
            touchHudView.saveControls();
        }
        spawnPlayModeViews();
    }

    private void updateSelectedControlSizeLabel() {
        if (tvSizeInfo == null || touchHudView == null) return;
        HudConfig.ControlDef selected = touchHudView.getSelectedControl();
        if (selected != null) {
            tvSizeInfo.setText("📐 [" + selected.label + "] " + selected.sizeDp + "dp");
        } else {
            tvSizeInfo.setText("📐 Select a button to resize");
        }
    }

    private void buildHudViewHierarchy() {
        hudRootLayout = new FrameLayout(this);

        // 1. Fullscreen Touch HUD View for Edit / Drag / Resize
        touchHudView = new GameHubTouchView(this);
        touchHudView.setOnControlSelectedListener(new GameHubTouchView.OnControlSelectedListener() {
            @Override
            public void onControlSelected(HudConfig.ControlDef def) {
                if (sizeScroll != null) sizeScroll.setVisibility(View.VISIBLE);
                updateSelectedControlSizeLabel();
            }
        });

        FrameLayout.LayoutParams touchLp = new FrameLayout.LayoutParams(
            FrameLayout.LayoutParams.MATCH_PARENT,
            FrameLayout.LayoutParams.MATCH_PARENT
        );
        hudRootLayout.addView(touchHudView, touchLp);

        // 2. Responsive Top GameHub Controller Dock inside HorizontalScrollView
        dockScroll = new HorizontalScrollView(this);
        dockScroll.setHorizontalScrollBarEnabled(false);
        dockScroll.setOverScrollMode(View.OVER_SCROLL_NEVER);
        dockScroll.setVisibility(View.VISIBLE);

        dockBar = new LinearLayout(this);
        dockBar.setOrientation(LinearLayout.HORIZONTAL);
        dockBar.setGravity(Gravity.CENTER_VERTICAL);
        dockBar.setPadding(dpToPx(10), dpToPx(6), dpToPx(10), dpToPx(6));
        applyRoundedCardBg(dockBar, 0xF00B121B, 0xFF00D2FF, dpToPx(24));

        btnDockSavePlay = createDockButton("💾 SAVE & PLAY", 0xFFFFFFFF, 0xFF2E7D32, 0xFF00E676);
        btnDockDone = createDockButton("✔ PLAY", 0xFF00D2FF, 0xDD121F2D, 0xFF00D2FF);
        btnDockKeyboard = createDockButton("⌨ KEYBOARD", 0xFFFFFFFF, 0xEE1E4466, 0xFF00D2FF);
        btnDockAdd = createDockButton("➕ ADD", 0xFF00D2FF, 0xDD121F2D, 0xFF00D2FF);
        btnDockRemove = createDockButton("🗑 REMOVE", 0xFFFF6B6B, 0xDD281418, 0xFFD83B3B);
        btnDockMove = createDockButton("📐 MOVE", 0xFF66C0F4, 0xDD121F2D, 0xFF284868);
        btnDockOrientation = createDockButton("🔄 AUTO", 0xFF66C0F4, 0xDD121F2D, 0xFF284868);
        btnDockOpacity = createDockButton("👁 75%", 0xFF66C0F4, 0xDD121F2D, 0xFF284868);
        btnDockPreset = createDockButton("🎮 PRESET", 0xFF66C0F4, 0xDD121F2D, 0xFF284868);
        btnDockHideAll = createDockButton("🚫 HIDE ALL", 0xFF9EABB8, 0xDD18202A, 0xFF3A4B5E);

        dockBar.addView(btnDockSavePlay);
        dockBar.addView(btnDockDone);
        dockBar.addView(btnDockKeyboard);
        dockBar.addView(btnDockAdd);
        dockBar.addView(btnDockRemove);
        dockBar.addView(btnDockMove);
        dockBar.addView(btnDockOrientation);
        dockBar.addView(btnDockOpacity);
        dockBar.addView(btnDockPreset);
        dockBar.addView(btnDockHideAll);

        dockScroll.addView(dockBar);

        FrameLayout.LayoutParams dockLp = new FrameLayout.LayoutParams(
            FrameLayout.LayoutParams.WRAP_CONTENT,
            FrameLayout.LayoutParams.WRAP_CONTENT
        );
        dockLp.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
        dockLp.topMargin = dpToPx(8);
        hudRootLayout.addView(dockScroll, dockLp);

        // 3. Bottom Floating Size Controller Bar
        buildSizeBarHierarchy();

        // 4. Add Control Modal Overlay
        buildAddControlOverlay();

        // Setup Dock Click Listeners
        btnDockSavePlay.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                touchHudView.saveControls();
                exitEditModeToPlay();
                Toast.makeText(GameHubOverlayService.this, "Layout Saved! Touch pass-through is active for Steam Link 🎮", Toast.LENGTH_SHORT).show();
            }
        });

        btnDockDone.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                exitEditModeToPlay();
            }
        });

        btnDockKeyboard.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                showKeyboardModal();
            }
        });

        btnDockAdd.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                showAddControlDialog();
            }
        });

        btnDockRemove.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                boolean nextRemove = !touchHudView.isRemoveMode();
                touchHudView.setRemoveMode(nextRemove);
                updateDockButtonStates();
                if (nextRemove) {
                    Toast.makeText(GameHubOverlayService.this, "Tap any button to delete it!", Toast.LENGTH_SHORT).show();
                }
            }
        });

        btnDockMove.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                boolean nextEdit = !touchHudView.isEditMode();
                touchHudView.setEditMode(nextEdit);
                updateDockButtonStates();
                if (nextEdit) {
                    if (sizeScroll != null) sizeScroll.setVisibility(View.VISIBLE);
                } else {
                    if (sizeScroll != null) sizeScroll.setVisibility(View.GONE);
                }
            }
        });

        btnDockOrientation.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                String next = HudConfig.cycleOrientationMode(GameHubOverlayService.this);
                applyOrientationMode();
                Toast.makeText(GameHubOverlayService.this, "Orientation: " + next, Toast.LENGTH_SHORT).show();
            }
        });

        btnDockOpacity.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                int cur = HudConfig.getPrefs(GameHubOverlayService.this).getInt(HudConfig.KEY_OPACITY, 1);
                cur = (cur + 1) % 4;
                HudConfig.getPrefs(GameHubOverlayService.this).edit().putInt(HudConfig.KEY_OPACITY, cur).apply();
                float newAlpha = HudConfig.getOpacity(GameHubOverlayService.this);
                touchHudView.setHudAlpha(newAlpha);
                if (floatingPill != null) floatingPill.setAlpha(newAlpha);
                if (miniPlayDock != null) miniPlayDock.setAlpha(newAlpha);
                updateDockButtonStates();
            }
        });

        btnDockPreset.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
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
                updateSelectedControlSizeLabel();
                Toast.makeText(GameHubOverlayService.this, "Loaded Preset: " + nextPreset, Toast.LENGTH_SHORT).show();
            }
        });

        btnDockHideAll.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                collapseHud();
            }
        });
    }

    private void buildSizeBarHierarchy() {
        sizeScroll = new HorizontalScrollView(this);
        sizeScroll.setHorizontalScrollBarEnabled(false);
        sizeScroll.setOverScrollMode(View.OVER_SCROLL_NEVER);
        sizeScroll.setVisibility(View.GONE);

        sizeBar = new LinearLayout(this);
        sizeBar.setOrientation(LinearLayout.HORIZONTAL);
        sizeBar.setGravity(Gravity.CENTER_VERTICAL);
        sizeBar.setPadding(dpToPx(12), dpToPx(6), dpToPx(12), dpToPx(6));
        applyRoundedCardBg(sizeBar, 0xF20B131D, 0xFFFFD600, dpToPx(24));

        tvSizeInfo = new TextView(this);
        tvSizeInfo.setText("📐 RESIZE BUTTON");
        tvSizeInfo.setTextColor(0xFFFFD600);
        tvSizeInfo.setTextSize(12f);
        tvSizeInfo.setTypeface(null, Typeface.BOLD);
        tvSizeInfo.setPadding(dpToPx(4), 0, dpToPx(10), 0);
        sizeBar.addView(tvSizeInfo);

        btnSizeMinus = createDockButton("➖ SMALLER (-6)", 0xFFFFFFFF, 0xDD28181A, 0xFFFF5252);
        btnSizePlus = createDockButton("➕ LARGER (+6)", 0xFFFFFFFF, 0xDD18281A, 0xFF00E676);

        btnSizeMinus.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                touchHudView.resizeSelectedControl(-6);
                updateSelectedControlSizeLabel();
            }
        });

        btnSizePlus.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                touchHudView.resizeSelectedControl(+6);
                updateSelectedControlSizeLabel();
            }
        });

        sizeBar.addView(btnSizeMinus);
        sizeBar.addView(btnSizePlus);

        Button btnS = createDockButton("S (42dp)", 0xFF8FA5B8, 0xDD1A2736, 0xFF2A425A);
        Button btnM = createDockButton("M (56dp)", 0xFF8FA5B8, 0xDD1A2736, 0xFF2A425A);
        Button btnL = createDockButton("L (72dp)", 0xFF8FA5B8, 0xDD1A2736, 0xFF2A425A);
        Button btnXL = createDockButton("XL (96dp)", 0xFF8FA5B8, 0xDD1A2736, 0xFF2A425A);
        Button btnMax = createDockButton("STICK (130dp)", 0xFF8FA5B8, 0xDD1A2736, 0xFF2A425A);

        btnS.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { touchHudView.setSelectedControlSize(42); updateSelectedControlSizeLabel(); } });
        btnM.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { touchHudView.setSelectedControlSize(56); updateSelectedControlSizeLabel(); } });
        btnL.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { touchHudView.setSelectedControlSize(72); updateSelectedControlSizeLabel(); } });
        btnXL.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { touchHudView.setSelectedControlSize(96); updateSelectedControlSizeLabel(); } });
        btnMax.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { touchHudView.setSelectedControlSize(130); updateSelectedControlSizeLabel(); } });

        sizeBar.addView(btnS);
        sizeBar.addView(btnM);
        sizeBar.addView(btnL);
        sizeBar.addView(btnXL);
        sizeBar.addView(btnMax);

        sizeScroll.addView(sizeBar);

        FrameLayout.LayoutParams sizeLp = new FrameLayout.LayoutParams(
            FrameLayout.LayoutParams.WRAP_CONTENT,
            FrameLayout.LayoutParams.WRAP_CONTENT
        );
        sizeLp.gravity = Gravity.BOTTOM | Gravity.CENTER_HORIZONTAL;
        sizeLp.bottomMargin = dpToPx(14);
        hudRootLayout.addView(sizeScroll, sizeLp);
    }

    private void updateDockButtonStates() {
        if (btnDockRemove != null) {
            boolean isRemove = touchHudView.isRemoveMode();
            btnDockRemove.setText(isRemove ? "🗑 REMOVE (ON)" : "🗑 REMOVE");
            applyRoundedCardBg(btnDockRemove, isRemove ? 0xFFE53935 : 0xDD281418, 
                               isRemove ? 0xFFFFFFFF : 0xFFD83B3B, dpToPx(16));
            btnDockRemove.setTextColor(isRemove ? 0xFFFFFFFF : 0xFFFF6B6B);
        }

        if (btnDockMove != null) {
            boolean isEdit = touchHudView.isEditMode();
            btnDockMove.setText(isEdit ? "📐 MOVE (ON)" : "📐 MOVE");
            applyRoundedCardBg(btnDockMove, isEdit ? 0xFF2E7D32 : 0xDD121F2D, 
                               isEdit ? 0xFF00E676 : 0xFF284868, dpToPx(16));
            btnDockMove.setTextColor(isEdit ? 0xFFFFFFFF : 0xFF66C0F4);
        }

        if (btnDockOpacity != null) {
            int pct = (int)(touchHudView.getHudAlpha() * 100);
            btnDockOpacity.setText("👁 " + pct + "%");
        }
        updateDockOrientationButton();
    }

    // ==========================================
    // IN-GAME FLOATING KEYBOARD MODAL
    // ==========================================
    private void showKeyboardModal() {
        if (keyboardModalView != null) {
            try { windowManager.removeView(keyboardModalView); } catch (Exception ignored) {}
            keyboardModalView = null;
        }

        DisplayMetrics dm = getResources().getDisplayMetrics();
        int width = Math.min((int)(dm.widthPixels * 0.90f), dpToPx(480));

        FrameLayout container = new FrameLayout(this);
        container.setBackgroundColor(0x00000000);

        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setPadding(dpToPx(16), dpToPx(14), dpToPx(16), dpToPx(14));
        applyRoundedCardBg(card, 0xF4101A26, 0xFF00D2FF, dpToPx(16));

        // Header
        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);

        TextView tvTitle = new TextView(this);
        tvTitle.setText("⌨ IN-GAME TEXT / CHAT");
        tvTitle.setTextSize(13f);
        tvTitle.setTypeface(null, Typeface.BOLD);
        tvTitle.setTextColor(0xFF00D2FF);
        LinearLayout.LayoutParams tLp = new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
        header.addView(tvTitle, tLp);

        TextView btnClose = new TextView(this);
        btnClose.setText("✕");
        btnClose.setTextSize(16f);
        btnClose.setTypeface(null, Typeface.BOLD);
        btnClose.setTextColor(0xFF888888);
        btnClose.setPadding(dpToPx(8), dpToPx(4), dpToPx(8), dpToPx(4));
        btnClose.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                hideKeyboardModal();
            }
        });
        header.addView(btnClose);
        card.addView(header);

        TextView tvSub = new TextView(this);
        tvSub.setText("Type character name, password, or chat directly into PC game");
        tvSub.setTextSize(10.5f);
        tvSub.setTextColor(0xFF8F98A0);
        tvSub.setPadding(0, dpToPx(2), 0, dpToPx(8));
        card.addView(tvSub);

        // EditText Field
        final EditText etInput = new EditText(this);
        etInput.setHint("Enter text / name here...");
        etInput.setHintTextColor(0xFF6B7A8C);
        etInput.setTextColor(0xFFF5F5F5);
        etInput.setTextSize(14f);
        etInput.setSingleLine(true);
        etInput.setPadding(dpToPx(12), dpToPx(10), dpToPx(12), dpToPx(10));
        applyRoundedCardBg(etInput, 0xFF172230, 0xFF00D2FF, dpToPx(8));
        card.addView(etInput);

        // Action Buttons Row: [ ⏎ SEND & ENTER ] and [ 💬 SEND TEXT ]
        LinearLayout actionRow = new LinearLayout(this);
        actionRow.setOrientation(LinearLayout.HORIZONTAL);
        actionRow.setPadding(0, dpToPx(10), 0, dpToPx(8));

        Button btnSendEnter = new Button(this);
        btnSendEnter.setText("⏎ SEND & ENTER");
        btnSendEnter.setTextSize(12f);
        btnSendEnter.setTypeface(null, Typeface.BOLD);
        btnSendEnter.setTextColor(0xFFFFFFFF);
        applyRoundedCardBg(btnSendEnter, 0xFF2E7D32, 0xFF00E676, dpToPx(8));
        LinearLayout.LayoutParams btnEnterLp = new LinearLayout.LayoutParams(0, dpToPx(42), 1.2f);
        btnEnterLp.rightMargin = dpToPx(8);
        btnSendEnter.setLayoutParams(btnEnterLp);
        btnSendEnter.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                String text = etInput.getText().toString();
                GameHubInputSender.getInstance(GameHubOverlayService.this).sendText(text, true);
                Toast.makeText(GameHubOverlayService.this, "Typed: \"" + text + "\" + [ENTER]", Toast.LENGTH_SHORT).show();
                hideKeyboardModal();
            }
        });
        actionRow.addView(btnSendEnter);

        Button btnSendOnly = new Button(this);
        btnSendOnly.setText("💬 SEND TEXT");
        btnSendOnly.setTextSize(12f);
        btnSendOnly.setTypeface(null, Typeface.BOLD);
        btnSendOnly.setTextColor(0xFF00D2FF);
        applyRoundedCardBg(btnSendOnly, 0xFF1E344A, 0xFF00D2FF, dpToPx(8));
        LinearLayout.LayoutParams btnSendLp = new LinearLayout.LayoutParams(0, dpToPx(42), 1f);
        btnSendOnly.setLayoutParams(btnSendLp);
        btnSendOnly.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                String text = etInput.getText().toString();
                GameHubInputSender.getInstance(GameHubOverlayService.this).sendText(text, false);
                Toast.makeText(GameHubOverlayService.this, "Typed: \"" + text + "\"", Toast.LENGTH_SHORT).show();
                hideKeyboardModal();
            }
        });
        actionRow.addView(btnSendOnly);
        card.addView(actionRow);

        // Quick Gaming Keys Row: [ ⌫ DEL ] [ ENTER ] [ TAB ] [ ESC ] [ SPACE ]
        LinearLayout keysRow = new LinearLayout(this);
        keysRow.setOrientation(LinearLayout.HORIZONTAL);
        keysRow.setGravity(Gravity.CENTER);

        String[] quickKeys = {"⌫ DEL", "ENTER", "TAB", "ESC", "SPACE"};
        final int[] quickCodes = {67, 66, 61, 111, 62};
        final String[] quickLabels = {"BACK", "ENTER", "TAB", "ESC", "SPACE"};

        for (int i = 0; i < quickKeys.length; i++) {
            final int code = quickCodes[i];
            final String lbl = quickLabels[i];
            Button btnKey = new Button(this);
            btnKey.setText(quickKeys[i]);
            btnKey.setTextSize(10f);
            btnKey.setTypeface(null, Typeface.BOLD);
            btnKey.setTextColor(0xFFC7D5E0);
            applyRoundedCardBg(btnKey, 0xFF14202C, 0xFF2A475E, dpToPx(6));
            LinearLayout.LayoutParams kLp = new LinearLayout.LayoutParams(0, dpToPx(34), 1f);
            if (i > 0) kLp.leftMargin = dpToPx(4);
            btnKey.setLayoutParams(kLp);
            btnKey.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    final HudConfig.ControlDef kDef = new HudConfig.ControlDef(lbl, "btn", lbl, 0xFF00D2FF, code, 0.5f, 0.5f, 50);
                    GameHubInputSender.getInstance(GameHubOverlayService.this).sendButton(kDef, true);
                    v.postDelayed(new Runnable() {
                        @Override
                        public void run() {
                            GameHubInputSender.getInstance(GameHubOverlayService.this).sendButton(kDef, false);
                        }
                    }, 50);
                }
            });
            keysRow.addView(btnKey);
        }
        card.addView(keysRow);

        container.addView(card, new FrameLayout.LayoutParams(width, FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.CENTER));

        int windowType = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
            ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
            : WindowManager.LayoutParams.TYPE_PHONE;

        keyboardModalParams = new WindowManager.LayoutParams(
            width,
            WindowManager.LayoutParams.WRAP_CONTENT,
            windowType,
            WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL, // Allow soft keyboard input focus
            PixelFormat.TRANSLUCENT
        );
        keyboardModalParams.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
        keyboardModalParams.y = dpToPx(30);

        keyboardModalView = container;
        windowManager.addView(keyboardModalView, keyboardModalParams);

        etInput.requestFocus();
        etInput.postDelayed(new Runnable() {
            @Override
            public void run() {
                InputMethodManager imm = (InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
                if (imm != null) {
                    imm.showSoftInput(etInput, InputMethodManager.SHOW_IMPLICIT);
                }
            }
        }, 150);
    }

    private void hideKeyboardModal() {
        if (keyboardModalView != null) {
            try {
                InputMethodManager imm = (InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
                if (imm != null) {
                    imm.hideSoftInputFromWindow(keyboardModalView.getWindowToken(), 0);
                }
                windowManager.removeView(keyboardModalView);
            } catch (Exception ignored) {}
            keyboardModalView = null;
        }
    }

    // ==========================================
    // ADD CONTROL OVERLAY
    // ==========================================
    private void buildAddControlOverlay() {
        addControlOverlay = new FrameLayout(this);
        addControlOverlay.setBackgroundColor(0x99000000);
        addControlOverlay.setVisibility(View.GONE);

        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setPadding(dpToPx(16), dpToPx(16), dpToPx(16), dpToPx(16));
        applyRoundedCardBg(card, 0xF4101A26, 0xFF00D2FF, dpToPx(20));

        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);
        header.setPadding(0, 0, 0, dpToPx(10));

        TextView tvTitle = new TextView(this);
        tvTitle.setText("🎮 ADD CONTROLLER BUTTON");
        tvTitle.setTextColor(0xFF00D2FF);
        tvTitle.setTextSize(15f);
        tvTitle.setTypeface(null, Typeface.BOLD);
        LinearLayout.LayoutParams titleLp = new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
        header.addView(tvTitle, titleLp);

        TextView btnClose = new TextView(this);
        btnClose.setText("✕ CLOSE");
        btnClose.setTextColor(0xFFFF5252);
        btnClose.setTextSize(12.5f);
        btnClose.setTypeface(null, Typeface.BOLD);
        btnClose.setPadding(dpToPx(10), dpToPx(4), dpToPx(10), dpToPx(4));
        applyRoundedCardBg(btnClose, 0x44FF5252, 0xFFFF5252, dpToPx(12));
        btnClose.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                addControlOverlay.setVisibility(View.GONE);
            }
        });
        header.addView(btnClose);
        card.addView(header);

        TextView tvSub = new TextView(this);
        tvSub.setText("Tap any component to add on-screen (drag to position):");
        tvSub.setTextColor(0xFF8FA5B8);
        tvSub.setTextSize(12f);
        tvSub.setPadding(0, 0, 0, dpToPx(10));
        card.addView(tvSub);

        HorizontalScrollView catScroll = new HorizontalScrollView(this);
        catScroll.setHorizontalScrollBarEnabled(false);
        catScroll.setOverScrollMode(View.OVER_SCROLL_NEVER);
        catScroll.setPadding(0, 0, 0, dpToPx(10));

        LinearLayout catBar = new LinearLayout(this);
        catBar.setOrientation(LinearLayout.HORIZONTAL);

        final Button btnCatGamepad = createCategoryChip("🎮 GAMEPAD", true);
        final Button btnCatAction = createCategoryChip("🎯 ACTION / FPS", false);
        final Button btnCatKeyboard = createCategoryChip("⌨ KEYBOARD A-Z", false);
        final Button btnCatNumbers = createCategoryChip("🔢 NUMBERS 0-9", false);
        final Button btnCatMouse = createCategoryChip("🖱 MOUSE", false);

        catBar.addView(btnCatGamepad);
        catBar.addView(btnCatAction);
        catBar.addView(btnCatKeyboard);
        catBar.addView(btnCatNumbers);
        catBar.addView(btnCatMouse);
        catScroll.addView(catBar);
        card.addView(catScroll);

        ScrollView itemScroll = new ScrollView(this);
        itemScroll.setOverScrollMode(View.OVER_SCROLL_NEVER);

        addItemsContainer = new LinearLayout(this);
        addItemsContainer.setOrientation(LinearLayout.VERTICAL);
        itemScroll.addView(addItemsContainer);

        LinearLayout.LayoutParams scrollLp = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT,
            dpToPx(280)
        );
        card.addView(itemScroll, scrollLp);

        View.OnClickListener catClickListener = new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                highlightCategoryChip(btnCatGamepad, v == btnCatGamepad);
                highlightCategoryChip(btnCatAction, v == btnCatAction);
                highlightCategoryChip(btnCatKeyboard, v == btnCatKeyboard);
                highlightCategoryChip(btnCatNumbers, v == btnCatNumbers);
                highlightCategoryChip(btnCatMouse, v == btnCatMouse);

                if (v == btnCatGamepad) populateCatalogItems(HudConfig.getGamepadCatalog());
                else if (v == btnCatAction) populateCatalogItems(HudConfig.getActionCatalog());
                else if (v == btnCatKeyboard) populateCatalogItems(HudConfig.getKeyboardAlphabetCatalog());
                else if (v == btnCatNumbers) populateCatalogItems(HudConfig.getNumbersCatalog());
                else if (v == btnCatMouse) populateCatalogItems(HudConfig.getMouseCatalog());
            }
        };

        btnCatGamepad.setOnClickListener(catClickListener);
        btnCatAction.setOnClickListener(catClickListener);
        btnCatKeyboard.setOnClickListener(catClickListener);
        btnCatNumbers.setOnClickListener(catClickListener);
        btnCatMouse.setOnClickListener(catClickListener);

        populateCatalogItems(HudConfig.getGamepadCatalog());

        FrameLayout.LayoutParams cardLp = new FrameLayout.LayoutParams(
            dpToPx(380),
            FrameLayout.LayoutParams.WRAP_CONTENT
        );
        cardLp.gravity = Gravity.CENTER;
        cardLp.leftMargin = dpToPx(20);
        cardLp.rightMargin = dpToPx(20);
        addControlOverlay.addView(card, cardLp);

        addControlOverlay.setOnTouchListener(new View.OnTouchListener() {
            @Override
            public boolean onTouch(View v, MotionEvent event) {
                if (event.getAction() == MotionEvent.ACTION_DOWN) {
                    addControlOverlay.setVisibility(View.GONE);
                    return true;
                }
                return false;
            }
        });
        card.setOnTouchListener(new View.OnTouchListener() {
            @Override
            public boolean onTouch(View v, MotionEvent event) {
                return true;
            }
        });

        hudRootLayout.addView(addControlOverlay, new FrameLayout.LayoutParams(
            FrameLayout.LayoutParams.MATCH_PARENT,
            FrameLayout.LayoutParams.MATCH_PARENT
        ));
    }

    private void showAddControlDialog() {
        if (addControlOverlay != null) {
            addControlOverlay.setVisibility(View.VISIBLE);
        }
    }

    private void populateCatalogItems(List<HudConfig.ControlDef> items) {
        addItemsContainer.removeAllViews();
        int cols = 3;
        LinearLayout currentRow = null;
        for (int i = 0; i < items.size(); i++) {
            if (i % cols == 0) {
                currentRow = new LinearLayout(this);
                currentRow.setOrientation(LinearLayout.HORIZONTAL);
                currentRow.setPadding(0, dpToPx(3), 0, dpToPx(3));
                addItemsContainer.addView(currentRow);
            }

            final HudConfig.ControlDef template = items.get(i);
            Button btnItem = new Button(this);
            btnItem.setText(template.label);
            btnItem.setTextSize(12f);
            btnItem.setTypeface(null, Typeface.BOLD);
            btnItem.setTextColor(0xFFF0F5FA);
            btnItem.setPadding(dpToPx(8), dpToPx(10), dpToPx(8), dpToPx(10));
            applyRoundedCardBg(btnItem, 0xCC1A2736, template.color, dpToPx(12));

            btnItem.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    HudConfig.ControlDef newDef = template.copy();
                    newDef.id = template.id + "_" + System.currentTimeMillis() % 1000;
                    float jitterX = (random.nextFloat() - 0.5f) * 0.16f;
                    float jitterY = (random.nextFloat() - 0.5f) * 0.16f;
                    newDef.xRatio = 0.50f + jitterX;
                    newDef.yRatio = 0.50f + jitterY;

                    touchHudView.addControl(newDef);
                    touchHudView.setEditMode(true);
                    updateDockButtonStates();
                    addControlOverlay.setVisibility(View.GONE);

                    if (sizeScroll != null) sizeScroll.setVisibility(View.VISIBLE);
                    updateSelectedControlSizeLabel();

                    Toast.makeText(GameHubOverlayService.this, 
                        "Added [" + newDef.label + "]! Drag to place, resize at bottom, tap [SAVE & PLAY] when done.", 
                        Toast.LENGTH_SHORT).show();
                }
            });

            LinearLayout.LayoutParams itemLp = new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
            itemLp.setMargins(dpToPx(4), dpToPx(3), dpToPx(4), dpToPx(3));
            currentRow.addView(btnItem, itemLp);
        }
    }

    private Button createDockButton(String text, int textColor, int bgColor, int borderColor) {
        Button btn = new Button(this);
        btn.setText(text);
        btn.setTextColor(textColor);
        btn.setTextSize(11f);
        btn.setTypeface(null, Typeface.BOLD);
        btn.setPadding(dpToPx(12), dpToPx(6), dpToPx(12), dpToPx(6));
        btn.setMinWidth(0);
        btn.setMinHeight(0);
        btn.setMinimumWidth(0);
        btn.setMinimumHeight(0);
        applyRoundedCardBg(btn, bgColor, borderColor, dpToPx(16));

        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.WRAP_CONTENT,
            LinearLayout.LayoutParams.WRAP_CONTENT
        );
        lp.setMargins(dpToPx(3), 0, dpToPx(3), 0);
        btn.setLayoutParams(lp);
        return btn;
    }

    private Button createCategoryChip(String text, boolean active) {
        Button btn = new Button(this);
        btn.setText(text);
        btn.setTextSize(10.5f);
        btn.setTypeface(null, Typeface.BOLD);
        btn.setPadding(dpToPx(10), dpToPx(4), dpToPx(10), dpToPx(4));
        btn.setMinWidth(0);
        btn.setMinHeight(0);
        highlightCategoryChip(btn, active);

        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.WRAP_CONTENT,
            LinearLayout.LayoutParams.WRAP_CONTENT
        );
        lp.setMargins(0, 0, dpToPx(6), 0);
        btn.setLayoutParams(lp);
        return btn;
    }

    private void highlightCategoryChip(Button btn, boolean active) {
        if (active) {
            btn.setTextColor(0xFF0E141D);
            applyRoundedCardBg(btn, 0xFF00D2FF, 0xFF00D2FF, dpToPx(12));
        } else {
            btn.setTextColor(0xFF8FA5B8);
            applyRoundedCardBg(btn, 0xCC1A2736, 0xFF2A425A, dpToPx(12));
        }
    }

    private void applyRoundedCardBg(View v, int bgColor, int borderColor, int radiusPx) {
        GradientDrawable gd = new GradientDrawable();
        gd.setShape(GradientDrawable.RECTANGLE);
        gd.setCornerRadius(radiusPx);
        gd.setColor(bgColor);
        gd.setStroke(dpToPx(1.5f), borderColor);
        v.setBackground(gd);
    }

    private int dpToPx(float dp) {
        DisplayMetrics metrics = getResources().getDisplayMetrics();
        return (int) (dp * metrics.density + 0.5f);
    }

    public void applyOrientationMode() {
        String mode = HudConfig.getOrientationMode(this);
        int requested = ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED;
        if (HudConfig.ORIENTATION_HORIZONTAL.equals(mode)) {
            requested = ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE;
        } else if (HudConfig.ORIENTATION_VERTICAL.equals(mode)) {
            requested = ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT;
        }

        if (hudParams != null) {
            hudParams.screenOrientation = requested;
            if (hudRootLayout != null && isEditModeActive) {
                try {
                    windowManager.updateViewLayout(hudRootLayout, hudParams);
                } catch (Throwable ignored) {}
            }
        }
        if (pillParams != null) {
            pillParams.screenOrientation = requested;
            if (floatingPill != null && !isHudExpanded) {
                try {
                    windowManager.updateViewLayout(floatingPill, pillParams);
                } catch (Throwable ignored) {}
            }
        }
        updateDockOrientationButton();
    }

    private void updateDockOrientationButton() {
        if (btnDockOrientation == null) return;
        String mode = HudConfig.getOrientationMode(this);
        if (HudConfig.ORIENTATION_HORIZONTAL.equals(mode)) {
            btnDockOrientation.setText("🔄 HORIZ");
        } else if (HudConfig.ORIENTATION_VERTICAL.equals(mode)) {
            btnDockOrientation.setText("🔄 VERT");
        } else {
            btnDockOrientation.setText("🔄 AUTO");
        }
    }

    @Override
    public void onConfigurationChanged(Configuration newConfig) {
        super.onConfigurationChanged(newConfig);
        if (isHudExpanded) {
            if (isEditModeActive) {
                if (hudRootLayout != null && hudParams != null) {
                    try { windowManager.updateViewLayout(hudRootLayout, hudParams); } catch (Throwable ignored) {}
                }
                if (touchHudView != null) {
                    touchHudView.post(new Runnable() {
                        @Override
                        public void run() {
                            touchHudView.requestLayout();
                            touchHudView.invalidate();
                        }
                    });
                }
            } else {
                // In play mode, reposition the discrete floating controls for the new orientation dimensions
                spawnPlayModeViews();
            }
        }
    }

    @Override
    public void onDestroy() {
        super.onDestroy();
        instance = null;
        clearPlayModeViews();
        hideKeyboardModal();
        if (floatingPill != null && floatingPill.isAttachedToWindow()) {
            try { windowManager.removeView(floatingPill); } catch (Exception ignored) {}
        }
        if (hudRootLayout != null && hudRootLayout.isAttachedToWindow()) {
            try { windowManager.removeView(hudRootLayout); } catch (Exception ignored) {}
        }
    }
}
