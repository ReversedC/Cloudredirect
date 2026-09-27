package com.cloudredirect.suolink;

import android.webkit.WebView;
import android.webkit.WebViewClient;

public class SuoWebViewClient extends WebViewClient {
    private final MainActivity activity;

    public SuoWebViewClient(MainActivity activity) {
        this.activity = activity;
    }

    @Override
    public void onPageFinished(WebView view, String url) {
        super.onPageFinished(view, url);
        activity.setConnected(true);
    }
}
