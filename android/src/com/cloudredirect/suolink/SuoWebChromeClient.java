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
                try {
                    Intent pickIntent = new Intent(Intent.ACTION_GET_CONTENT);
                    pickIntent.setType("image/*");
                    activity.startActivityForResult(Intent.createChooser(pickIntent, "Select QR Image"), MainActivity.REQUEST_CODE_FILE_CHOOSER);
                    return true;
                } catch (Exception ex2) {
                    activity.setFilePathCallback(null);
                }
            }
        }
        return false;
    }

    @Override
    public boolean onConsoleMessage(android.webkit.ConsoleMessage consoleMessage) {
        android.util.Log.d("SUO_LINK_JS", consoleMessage.message() + " (" + consoleMessage.sourceId() + ":" + consoleMessage.lineNumber() + ")");
        return true;
    }

    @Override
    public boolean onJsAlert(WebView view, String url, String message, android.webkit.JsResult result) {
        if (activity != null) {
            new android.app.AlertDialog.Builder(activity)
                    .setTitle("SUO Link")
                    .setMessage(message)
                    .setPositiveButton(android.R.string.ok, null)
                    .show();
            result.confirm();
            return true;
        }
        return false;
    }
}
