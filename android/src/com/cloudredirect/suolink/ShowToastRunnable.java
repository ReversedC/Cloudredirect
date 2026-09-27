package com.cloudredirect.suolink;

import android.widget.Toast;

public class ShowToastRunnable implements Runnable {
    private final MainActivity activity;
    private final String message;

    public ShowToastRunnable(MainActivity activity, String message) {
        this.activity = activity;
        this.message = message;
    }

    @Override
    public void run() {
        if (activity != null && message != null) {
            Toast.makeText(activity, message, Toast.LENGTH_SHORT).show();
        }
    }
}
