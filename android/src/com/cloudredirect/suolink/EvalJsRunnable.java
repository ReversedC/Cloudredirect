package com.cloudredirect.suolink;

import android.webkit.WebView;

public class EvalJsRunnable implements Runnable {
    private final WebView wv;
    private final String script;

    public EvalJsRunnable(WebView wv, String script) {
        this.wv = wv;
        this.script = script;
    }

    @Override
    public void run() {
        if (wv != null && script != null) {
            wv.evaluateJavascript(script, null);
        }
    }
}
