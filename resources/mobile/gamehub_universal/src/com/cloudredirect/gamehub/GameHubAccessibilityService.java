package com.cloudredirect.gamehub;

import android.accessibilityservice.AccessibilityService;
import android.accessibilityservice.GestureDescription;
import android.graphics.Path;
import android.os.Build;
import android.view.accessibility.AccessibilityEvent;

public class GameHubAccessibilityService extends AccessibilityService {
    private static GameHubAccessibilityService instance;

    public static boolean isAvailable() {
        return instance != null;
    }

    public static void dispatchTap(float x, float y) {
        if (instance == null || Build.VERSION.SDK_INT < Build.VERSION_CODES.N) return;
        try {
            Path path = new Path();
            path.moveTo(x, y);
            GestureDescription.StrokeDescription stroke =
                new GestureDescription.StrokeDescription(path, 0, 40);
            GestureDescription.Builder builder = new GestureDescription.Builder();
            builder.addStroke(stroke);
            instance.dispatchGesture(builder.build(), null, null);
        } catch (Exception ignored) {}
    }

    @Override
    public void onServiceConnected() {
        super.onServiceConnected();
        instance = this;
    }

    @Override
    public void onAccessibilityEvent(AccessibilityEvent event) {}

    @Override
    public void onInterrupt() {}

    @Override
    public void onDestroy() {
        super.onDestroy();
        instance = null;
    }
}
