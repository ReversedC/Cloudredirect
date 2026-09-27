package com.cloudredirect.suolink;

import android.content.Intent;
import android.net.Uri;
import android.webkit.PermissionRequest;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebView;

public class SuoWebChromeClient extends WebChromeClient {
    private final MainActivity activity;

    public SuoWebChromeClient(MainActivity activity) {
        this.activity = activity;
    }

    @Override
    public void onPermissionRequest(PermissionRequest request) {
        if (request != null) {
            request.grant(request.getResources());
        }
    }

    @Override
    public boolean onShowFileChooser(WebView webView, ValueCallback<Uri[]> filePathCallback, FileChooserParams fileChooserParams) {
        if (activity != null) {
            activity.setFilePathCallback(filePathCallback);
            try {
                Intent intent = fileChooserParams.createIntent();
                activity.startActivityForResult(intent, MainActivity.REQUEST_CODE_FILE_CHOOSER);
                return true;
            } catch (Exception ex) {
                activity.setFilePathCallback(null);
            }
        }
        return false;
    }
}
