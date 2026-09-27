package com.cloudredirect.suolink;

import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.webkit.JavascriptInterface;

public class SuoNativeBridge {
    private final Vibrator vibrator;

    public SuoNativeBridge(Vibrator vibrator) {
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
}
