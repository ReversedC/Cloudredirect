package com.cloudredirect.suolink;

public class LoadUrlRunnable implements Runnable {
    private final MainActivity activity;
    private final String url;

    public LoadUrlRunnable(MainActivity activity, String url) {
        this.activity = activity;
        this.url = url;
    }

    @Override
    public void run() {
        if (activity != null && url != null && !url.trim().isEmpty()) {
            activity.loadHostUrl(url.trim());
        }
    }
}
