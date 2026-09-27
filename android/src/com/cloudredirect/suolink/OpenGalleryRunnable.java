package com.cloudredirect.suolink;

public class OpenGalleryRunnable implements Runnable {
    private final MainActivity activity;

    public OpenGalleryRunnable(MainActivity activity) {
        this.activity = activity;
    }

    @Override
    public void run() {
        if (activity != null) {
            activity.openGalleryPicker();
        }
    }
}
