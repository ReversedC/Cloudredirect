package com.cloudredirect.suolink;

public class ShowDiscoveryRunnable implements Runnable {
    private final MainActivity activity;

    public ShowDiscoveryRunnable(MainActivity activity) {
        this.activity = activity;
    }

    @Override
    public void run() {
        if (activity != null) {
            activity.showDiscoveryPage();
        }
    }
}
