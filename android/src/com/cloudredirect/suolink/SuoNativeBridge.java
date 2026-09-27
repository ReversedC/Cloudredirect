package com.cloudredirect.suolink;

import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.webkit.JavascriptInterface;

public class SuoNativeBridge {
    private final MainActivity activity;
    private final Vibrator vibrator;

    public SuoNativeBridge(MainActivity activity, Vibrator vibrator) {
        this.activity = activity;
        this.vibrator = vibrator;
    }

    @JavascriptInterface
    public void vibrate(int durationMs) {
        if (vibrator != null) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                vibrator.vibrate(VibrationEffect.createOneShot(durationMs, VibrationEffect.DEFAULT_AMPLITUDE));
            } else {
                vibrator.vibrate(durationMs);
            }
        }
    }

    @JavascriptInterface
    public void showDiscovery() {
        if (activity != null) {
            activity.runOnUiThread(new ShowDiscoveryRunnable(activity));
        }
    }

    @JavascriptInterface
    public void saveAndLoadHost(final String url) {
        if (activity != null && url != null) {
            activity.runOnUiThread(new LoadUrlRunnable(activity, url));
        }
    }

    @JavascriptInterface
    public void connectSmart(final String url) {
        if (activity != null && url != null) {
            activity.connectSmart(url);
        }
    }

    @JavascriptInterface
    public void openCameraScanner() {
        if (activity != null) {
            activity.runOnUiThread(new OpenCameraRunnable(activity));
        }
    }

    @JavascriptInterface
    public void openGalleryPicker() {
        if (activity != null) {
            activity.runOnUiThread(new OpenGalleryRunnable(activity));
        }
    }
}
