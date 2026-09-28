package com.cloudredirect.gamehub;

import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.text.Editable;
import android.text.TextWatcher;
import android.view.View;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.CompoundButton;
import android.widget.EditText;
import android.widget.TextView;
import android.widget.Toast;

public class MainActivity extends Activity {
    private static final int REQUEST_OVERLAY_PERMISSION = 1204;

    private Button btnToggleOverlay;
    private Button btnGrantOverlay;
    private Button btnLaunchSteamLink;
    private Button btnLaunchMoonlight;
    private Button btnCyclePreset;
    private Button btnCycleOpacity;
    private Button btnResetLayout;
    private CheckBox cbHaptics;
    private TextView statusPill;

    private EditText editHostIp;
    private Button btnScanPc;
    private Button btnTestPing;
    private TextView textPcStatus;

    private GameHubInputSender inputSender;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        inputSender = GameHubInputSender.getInstance(this);

        btnToggleOverlay = (Button) findViewById(R.id.btnToggleOverlay);
        btnGrantOverlay = (Button) findViewById(R.id.btnGrantOverlay);
        btnLaunchSteamLink = (Button) findViewById(R.id.btnLaunchSteamLink);
        btnLaunchMoonlight = (Button) findViewById(R.id.btnLaunchMoonlight);
        btnCyclePreset = (Button) findViewById(R.id.btnCyclePreset);
        btnCycleOpacity = (Button) findViewById(R.id.btnCycleOpacity);
        btnResetLayout = (Button) findViewById(R.id.btnResetLayout);
        cbHaptics = (CheckBox) findViewById(R.id.cbHaptics);
        statusPill = (TextView) findViewById(R.id.statusPill);

        Button btnCheckUpdates = (Button) findViewById(R.id.btnCheckUpdates);
        if (btnCheckUpdates != null) {
            btnCheckUpdates.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    AutoUpdateService.checkManual(MainActivity.this);
                }
            });
        }

        // Automatic background update check on app launch
        AutoUpdateService.checkOnLaunch(this);

        editHostIp = (EditText) findViewById(R.id.editHostIp);
        btnScanPc = (Button) findViewById(R.id.btnScanPc);
        btnTestPing = (Button) findViewById(R.id.btnTestPing);
        textPcStatus = (TextView) findViewById(R.id.textPcStatus);

        btnToggleOverlay.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                toggleOverlayService();
            }
        });

        btnGrantOverlay.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                requestOverlayPermission();
            }
        });

        btnLaunchSteamLink.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                launchApp("com.valvesoftware.steamlink", "Steam Link");
            }
        });

        btnLaunchMoonlight.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                launchApp("com.limelight", "Moonlight / CloudRedirect Stream");
            }
        });

        btnCyclePreset.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                cyclePreset();
            }
        });

        btnCycleOpacity.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                cycleOpacity();
            }
        });

        btnResetLayout.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                HudConfig.resetControls(MainActivity.this);
                Toast.makeText(MainActivity.this, "Layout reset to defaults!", Toast.LENGTH_SHORT).show();
            }
        });

        cbHaptics.setChecked(HudConfig.isHapticsEnabled(this));
        cbHaptics.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override
            public void onCheckedChanged(CompoundButton buttonView, boolean isChecked) {
                HudConfig.getPrefs(MainActivity.this).edit().putBoolean(HudConfig.KEY_HAPTICS, isChecked).apply();
            }
        });

        // Setup PC Direct UDP listeners
        if (editHostIp != null) {
            editHostIp.setText(inputSender.getHostIp());
            editHostIp.addTextChangedListener(new TextWatcher() {
                @Override public void beforeTextChanged(CharSequence s, int start, int count, int after) {}
                @Override public void onTextChanged(CharSequence s, int start, int before, int count) {}
                @Override
                public void afterTextChanged(Editable s) {
                    inputSender.setHostIp(s.toString().trim());
                }
            });
        }

        if (btnScanPc != null) {
            btnScanPc.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    if (textPcStatus != null) {
                        textPcStatus.setText("Scanning WiFi...");
                        textPcStatus.setTextColor(0xFF00D2FF);
                    }
                    inputSender.startDiscovery(new GameHubInputSender.DiscoveryCallback() {
                        @Override
                        public void onDeviceFound(String hostName, String ipAddress) {
                            if (editHostIp != null) editHostIp.setText(ipAddress);
                            if (textPcStatus != null) {
                                textPcStatus.setText("Connected: " + hostName + " (" + ipAddress + ")");
                                textPcStatus.setTextColor(0xFF66D18F);
                            }
                        }

                        @Override
                        public void onTimeout() {
                            if (textPcStatus != null) {
                                textPcStatus.setText("Scan timed out. Enter IP manually.");
                                textPcStatus.setTextColor(0xFFF0AD4E);
                            }
                        }
                    });
                }
            });
        }

        if (btnTestPing != null) {
            btnTestPing.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    if (textPcStatus != null) {
                        textPcStatus.setText("Pinging PC UDP 48999...");
                    }
                    inputSender.sendPing(new Runnable() {
                        @Override
                        public void run() {
                            if (textPcStatus != null) {
                                textPcStatus.setText("UDP Ping Sent (<1ms latency)!");
                                textPcStatus.setTextColor(0xFF66D18F);
                            }
                        }
                    });
                }
            });
        }

        // Auto-discover host PC on app launch
        inputSender.startDiscovery(new GameHubInputSender.DiscoveryCallback() {
            @Override
            public void onDeviceFound(String hostName, String ipAddress) {
                if (editHostIp != null) editHostIp.setText(ipAddress);
                if (textPcStatus != null) {
                    textPcStatus.setText("Connected: " + hostName + " (" + ipAddress + ")");
                    textPcStatus.setTextColor(0xFF66D18F);
                }
            }

            @Override
            public void onTimeout() {}
        });

        updateUI();
    }

    @Override
    protected void onResume() {
        super.onResume();
        updateUI();
    }

    private void updateUI() {
        boolean hasOverlayPermission = hasOverlayPermission();
        if (hasOverlayPermission) {
            btnGrantOverlay.setText("✓ Overlay Permission Granted");
            btnGrantOverlay.setEnabled(false);
            btnGrantOverlay.setTextColor(0xFF66D18F);
        } else {
            btnGrantOverlay.setText("Grant Overlay Permission");
            btnGrantOverlay.setEnabled(true);
            btnGrantOverlay.setTextColor(0xFF66C0F4);
        }

        boolean isRunning = GameHubOverlayService.isRunning();
        if (isRunning) {
            btnToggleOverlay.setText("■  STOP GAMEHUB OVERLAY");
            btnToggleOverlay.setBackgroundResource(R.drawable.steam_btn_secondary);
            statusPill.setText("ACTIVE");
            statusPill.setTextColor(0xFF66D18F);
        } else {
            btnToggleOverlay.setText("▶  START GAMEHUB OVERLAY");
            btnToggleOverlay.setBackgroundResource(R.drawable.steam_btn_play);
            statusPill.setText("READY");
            statusPill.setTextColor(0xFFA4D007);
        }

        String preset = HudConfig.getPrefs(this).getString(HudConfig.KEY_PRESET, HudConfig.PRESET_STEAM_LINK);
        btnCyclePreset.setText("Preset: " + preset);

        int opacityPct = (int)(HudConfig.getOpacity(this) * 100);
        btnCycleOpacity.setText("Opacity: " + opacityPct + "%");
    }

    private boolean hasOverlayPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            return Settings.canDrawOverlays(this);
        }
        return true;
    }

    private void requestOverlayPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            Intent intent = new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                    Uri.parse("package:" + getPackageName()));
            startActivityForResult(intent, REQUEST_OVERLAY_PERMISSION);
        }
    }

    private void toggleOverlayService() {
        if (!hasOverlayPermission()) {
            Toast.makeText(this, "Please grant Overlay Permission first!", Toast.LENGTH_LONG).show();
            requestOverlayPermission();
            return;
        }

        if (GameHubOverlayService.isRunning()) {
            GameHubOverlayService.stopService(this);
            Toast.makeText(this, "GameHub Overlay Stopped", Toast.LENGTH_SHORT).show();
        } else {
            Intent serviceIntent = new Intent(this, GameHubOverlayService.class);
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                startForegroundService(serviceIntent);
            } else {
                startService(serviceIntent);
            }
            Toast.makeText(this, "GameHub Overlay Started! Floating [🎮 HUD] badge is ready for Steam Link.", Toast.LENGTH_LONG).show();
        }
        updateUI();
    }

    private void cyclePreset() {
        String cur = HudConfig.getPrefs(this).getString(HudConfig.KEY_PRESET, HudConfig.PRESET_STEAM_LINK);
        String next;
        if (HudConfig.PRESET_STEAM_LINK.equals(cur)) {
            next = HudConfig.PRESET_ACTION_RPG;
        } else if (HudConfig.PRESET_ACTION_RPG.equals(cur)) {
            next = HudConfig.PRESET_FPS;
        } else {
            next = HudConfig.PRESET_STEAM_LINK;
        }
        HudConfig.getPrefs(this).edit().putString(HudConfig.KEY_PRESET, next).apply();
        HudConfig.resetControls(this);
        updateUI();
        Toast.makeText(this, "Preset changed to: " + next, Toast.LENGTH_SHORT).show();
    }

    private void cycleOpacity() {
        int cur = HudConfig.getPrefs(this).getInt(HudConfig.KEY_OPACITY, 1);
        cur = (cur + 1) % 4;
        HudConfig.getPrefs(this).edit().putInt(HudConfig.KEY_OPACITY, cur).apply();
        updateUI();
    }

    private void launchApp(String packageName, String appName) {
        PackageManager pm = getPackageManager();
        Intent launchIntent = pm.getLaunchIntentForPackage(packageName);
        if (launchIntent != null) {
            startActivity(launchIntent);
        } else {
            try {
                Intent marketIntent = new Intent(Intent.ACTION_VIEW, Uri.parse("market://details?id=" + packageName));
                startActivity(marketIntent);
            } catch (Exception e) {
                Intent webIntent = new Intent(Intent.ACTION_VIEW, Uri.parse("https://play.google.com/store/apps/details?id=" + packageName));
                startActivity(webIntent);
            }
        }
    }
}
