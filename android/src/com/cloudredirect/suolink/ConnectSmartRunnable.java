package com.cloudredirect.suolink;

public class ConnectSmartRunnable implements Runnable {
    private final MainActivity activity;
    private final String url;

    public ConnectSmartRunnable(MainActivity activity, String url) {
        this.activity = activity;
        this.url = url;
    }

    @Override
    public void run() {
        if (activity != null && url != null) {
            activity.connectSmart(url);
        }
    }
}
