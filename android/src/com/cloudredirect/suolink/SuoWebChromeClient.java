package com.cloudredirect.suolink;

import android.webkit.PermissionRequest;
import android.webkit.WebChromeClient;

public class SuoWebChromeClient extends WebChromeClient {
    @Override
    public void onPermissionRequest(PermissionRequest request) {
        if (request != null) {
            request.grant(request.getResources());
        }
    }
}
