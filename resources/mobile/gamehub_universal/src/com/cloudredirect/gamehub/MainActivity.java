package com.cloudredirect.gamehub;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.pm.ActivityInfo;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.CompoundButton;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

public class MainActivity extends Activity {
    private static final int REQUEST_OVERLAY_PERMISSION = 1204;

    private Button btnToggleOverlay;
    private Button btnGrantOverlay;
    private Button btnCyclePreset;
    private Button btnCycleOpacity;
    private Button btnCycleOrientation;
    private Button btnResetLayout;
    private CheckBox cbHaptics;
    private TextView statusPill;

    // PC Connection UI
    private TextView tvPcStatusBadge;
    private TextView tvHostPcName;
    private TextView tvHostPcIp;
    private Button btnScanPc;
    private Button btnSetIp;

    private GameHubInputSender inputSender;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        inputSender = GameHubInputSender.getInstance(this);

        btnToggleOverlay = (Button) findViewById(R.id.btnToggleOverlay);
        btnGrantOverlay = (Button) findViewById(R.id.btnGrantOverlay);
        btnCyclePreset = (Button) findViewById(R.id.btnCyclePreset);
        btnCycleOpacity = (Button) findViewById(R.id.btnCycleOpacity);
        btnCycleOrientation = (Button) findViewById(R.id.btnCycleOrientation);
        btnResetLayout = (Button) findViewById(R.id.btnResetLayout);
        cbHaptics = (CheckBox) findViewById(R.id.cbHaptics);
        statusPill = (TextView) findViewById(R.id.statusPill);

        // PC Connection Views
        tvPcStatusBadge = (TextView) findViewById(R.id.tvPcStatusBadge);
        tvHostPcName = (TextView) findViewById(R.id.tvHostPcName);
        tvHostPcIp = (TextView) findViewById(R.id.tvHostPcIp);
        btnScanPc = (Button) findViewById(R.id.btnScanPc);
        btnSetIp = (Button) findViewById(R.id.btnSetIp);

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

        // PC Auto-Scan
        if (btnScanPc != null) {
            btnScanPc.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    triggerPcScan();
                }
            });
        }

        // Set IP Dialog
        if (btnSetIp != null) {
            btnSetIp.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    showSetIpDialog();
                }
            });
        }

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

        btnCycleOrientation.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                String next = HudConfig.cycleOrientationMode(MainActivity.this);
                applyOrientation();
                updateUI();
                Toast.makeText(MainActivity.this, "Orientation: " + next, Toast.LENGTH_SHORT).show();
                if (GameHubOverlayService.isRunning() && GameHubOverlayService.getInstance() != null) {
                    GameHubOverlayService.getInstance().applyOrientationMode();
                }
            }
        });

        applyOrientation();

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

        // Background initial scan
        triggerPcScan();

        updateUI();
    }

    @Override
    protected void onResume() {
        super.onResume();
        applyOrientation();
        updateUI();
        updateConnectionUI();
        AutoUpdateService.checkPendingInstallOnResume(this);
    }

    private void triggerPcScan() {
        if (btnScanPc != null) {
            btnScanPc.setText("🔍 Scanning...");
            btnScanPc.setEnabled(false);
        }
        if (tvPcStatusBadge != null) {
            tvPcStatusBadge.setText("SCANNING...");
            tvPcStatusBadge.setTextColor(0xFF00D2FF);
        }

        inputSender.startDiscovery(new GameHubInputSender.DiscoveryCallback() {
            @Override
            public void onDeviceFound(String hostName, String ipAddress) {
                if (btnScanPc != null) {
                    btnScanPc.setText("🔍 Auto-Scan PC");
                    btnScanPc.setEnabled(true);
                }
                updateConnectionUI();
                Toast.makeText(MainActivity.this, "Connected to: " + hostName + " (" + ipAddress + ")", Toast.LENGTH_SHORT).show();
            }

            @Override
            public void onTimeout() {
                if (btnScanPc != null) {
                    btnScanPc.setText("🔍 Auto-Scan PC");
                    btnScanPc.setEnabled(true);
                }
                updateConnectionUI();
            }
        });
    }

    private void updateConnectionUI() {
        String ip = inputSender.getHostIp();
        String name = inputSender.getLastPcName();

        if (tvHostPcName != null) {
            tvHostPcName.setText(name != null && !name.isEmpty() ? name : "CloudRedirect PC");
        }

        if (tvHostPcIp != null) {
            tvHostPcIp.setText("IP: " + (ip != null ? ip : "Not Configured") + " • UDP Port 48999");
        }

        if (ip != null && !ip.isEmpty()) {
            inputSender.testPing(ip, new GameHubInputSender.PingCallback() {
                @Override
                public void onSuccess(int latencyMs) {
                    if (tvPcStatusBadge != null) {
                        tvPcStatusBadge.setText("🟢 " + latencyMs + "ms");
                        tvPcStatusBadge.setTextColor(0xFF66D18F);
                    }
                    if (tvHostPcIp != null) {
                        tvHostPcIp.setText("IP: " + ip + " • Connected (" + latencyMs + "ms) • Port 48999");
                    }
                }

                @Override
                public void onFailure(String error) {
                    if (tvPcStatusBadge != null) {
                        tvPcStatusBadge.setText("🔴 OFFLINE");
                        tvPcStatusBadge.setTextColor(0xFFFF6B6B);
                    }
                }
            });
        } else {
            if (tvPcStatusBadge != null) {
                tvPcStatusBadge.setText("🔴 UNSET");
                tvPcStatusBadge.setTextColor(0xFFFF6B6B);
            }
        }
    }

    private void showSetIpDialog() {
        AlertDialog.Builder builder = new AlertDialog.Builder(this, AlertDialog.THEME_DEVICE_DEFAULT_DARK);
        builder.setTitle("Configure Host PC IP");

        LinearLayout container = new LinearLayout(this);
        container.setOrientation(LinearLayout.VERTICAL);
        container.setPadding(dpToPx(20), dpToPx(12), dpToPx(20), dpToPx(12));

        TextView tvDesc = new TextView(this);
        tvDesc.setText("Enter the local IP address of your Windows PC running CloudRedirect (e.g. 192.168.1.15):");
        tvDesc.setTextColor(0xFFC7D5E0);
        tvDesc.setTextSize(13f);
        tvDesc.setPadding(0, 0, 0, dpToPx(8));
        container.addView(tvDesc);

        final EditText etIp = new EditText(this);
        etIp.setText(inputSender.getHostIp());
        etIp.setTextColor(0xFFFFFFFF);
        etIp.setTextSize(15f);
        etIp.setSingleLine(true);
        container.addView(etIp);

        final TextView tvPingResult = new TextView(this);
        tvPingResult.setTextSize(12f);
        tvPingResult.setTextColor(0xFF00D2FF);
        tvPingResult.setPadding(0, dpToPx(8), 0, 0);
        container.addView(tvPingResult);

        builder.setView(container);

        builder.setPositiveButton("SAVE & CONNECT", new DialogInterface.OnClickListener() {
            @Override
            public void onClick(DialogInterface dialog, int which) {
                String newIp = etIp.getText().toString().trim();
                if (!newIp.isEmpty()) {
                    inputSender.setHostIp(newIp);
                    updateConnectionUI();
                    Toast.makeText(MainActivity.this, "Host IP set to: " + newIp, Toast.LENGTH_SHORT).show();
                }
            }
        });

        builder.setNeutralButton("TEST PING", null); // Override below to keep dialog open

        builder.setNegativeButton("CANCEL", null);

        final AlertDialog dialog = builder.create();
        dialog.show();

        // Custom listener for TEST PING to prevent automatic dialog dismiss
        Button btnPing = dialog.getButton(AlertDialog.BUTTON_NEUTRAL);
        if (btnPing != null) {
            btnPing.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    final String testIp = etIp.getText().toString().trim();
                    if (testIp.isEmpty()) {
                        tvPingResult.setText("Please enter an IP address first");
                        tvPingResult.setTextColor(0xFFFF6B6B);
                        return;
                    }
                    tvPingResult.setText("Pinging " + testIp + "...");
                    tvPingResult.setTextColor(0xFF00D2FF);

                    inputSender.testPing(testIp, new GameHubInputSender.PingCallback() {
                        @Override
                        public void onSuccess(int latencyMs) {
                            tvPingResult.setText("✓ Success! PC responded in " + latencyMs + "ms");
                            tvPingResult.setTextColor(0xFF66D18F);
                        }

                        @Override
                        public void onFailure(String error) {
                            tvPingResult.setText("✗ No response from PC on port 48999 (" + error + ")");
                            tvPingResult.setTextColor(0xFFFF6B6B);
                        }
                    });
                }
            });
        }
    }

    private int dpToPx(int dp) {
        return (int) (dp * getResources().getDisplayMetrics().density + 0.5f);
    }

    private void applyOrientation() {
        String mode = HudConfig.getOrientationMode(this);
        if (HudConfig.ORIENTATION_HORIZONTAL.equals(mode)) {
            setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE);
        } else if (HudConfig.ORIENTATION_VERTICAL.equals(mode)) {
            setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT);
        } else {
            setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED);
        }
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

        if (btnCycleOrientation != null) {
            btnCycleOrientation.setText("Orientation: " + HudConfig.getOrientationMode(this));
        }

        updateConnectionUI();
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
}
