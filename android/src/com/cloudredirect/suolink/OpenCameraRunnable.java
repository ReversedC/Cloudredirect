package com.cloudredirect.suolink;

public class OpenCameraRunnable implements Runnable {
    private final MainActivity activity;

    public OpenCameraRunnable(MainActivity activity) {
        this.activity = activity;
    }

    @Override
    public void run() {
        if (activity != null) {
            activity.startNativeCameraCapture();
        }
    }
}
