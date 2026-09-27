package com.cloudredirect.suolink;

public class OnUpdateFoundRunnable implements Runnable {
    private final MainActivity activity;
    private final String version;
    private final String apkUrl;

    public OnUpdateFoundRunnable(MainActivity activity, String version, String apkUrl) {
        this.activity = activity;
        this.version = version;
        this.apkUrl = apkUrl;
    }

    @Override
    public void run() {
        if (activity != null) {
            activity.onUpdateAvailable(version, apkUrl);
        }
    }
}
