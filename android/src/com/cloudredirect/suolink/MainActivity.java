package com.cloudredirect.suolink;

import android.app.Activity;
import android.content.Context;
import android.content.SharedPreferences;
import android.os.Build;
import android.os.Bundle;
import android.os.Vibrator;
import android.view.InputDevice;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.widget.FrameLayout;

public class MainActivity extends Activity {

    private static final String PREF_KEY_LAST_HOST = "last_host_url";

    private WebView webView;
    private Vibrator vibrator;
    private SharedPreferences prefs;
    private boolean connected = false;
    private Thread beaconListenerThread;
    private volatile boolean running = true;

    public boolean isConnected() { return connected; }
    public void setConnected(boolean val) { this.connected = val; }
    public boolean isRunning() { return running; }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        requestWindowFeature(Window.FEATURE_NO_TITLE);
        getWindow().setFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN,
                WindowManager.LayoutParams.FLAG_FULLSCREEN);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        setImmersiveMode();

        prefs = getSharedPreferences("suo_link_prefs", MODE_PRIVATE);
        vibrator = (Vibrator) getSystemService(Context.VIBRATOR_SERVICE);

        FrameLayout rootLayout = new FrameLayout(this);
        rootLayout.setBackgroundColor(0xFF0B0E14);

        webView = new WebView(this);
        webView.setBackgroundColor(0xFF0B0E14);
        configureWebView(webView);

        rootLayout.addView(webView, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        setContentView(rootLayout);

        String lastHost = prefs.getString(PREF_KEY_LAST_HOST, null);
        if (lastHost != null && !lastHost.isEmpty()) {
            loadHostUrl(lastHost);
        } else {
            showDiscoveryPage();
        }

        startBeaconDiscovery();
    }

    private void configureWebView(WebView wv) {
        WebSettings ws = wv.getSettings();
        ws.setJavaScriptEnabled(true);
        ws.setDomStorageEnabled(true);
        ws.setDatabaseEnabled(true);
        ws.setMediaPlaybackRequiresUserGesture(false);
        ws.setCacheMode(WebSettings.LOAD_NO_CACHE);
        ws.setAllowFileAccess(true);
        ws.setAllowContentAccess(true);
        ws.setUseWideViewPort(true);
        ws.setLoadWithOverviewMode(true);

        wv.setWebChromeClient(new WebChromeClient());
        wv.setWebViewClient(new SuoWebViewClient(this));
        wv.addJavascriptInterface(new SuoNativeBridge(vibrator), "SuoNative");
    }

    private void showDiscoveryPage() {
        String html = "<!DOCTYPE html><html><head><meta name='viewport' content='width=device-width, initial-scale=1.0'>" +
                "<style>body{background:#0b0e14;color:#f1f5f9;font-family:sans-serif;display:flex;flex-direction:column;" +
                "align-items:center;justify-content:center;height:100vh;margin:0;padding:20px;box-sizing:border-box;text-align:center;}" +
                "h1{color:#00d2ff;font-size:28px;margin-bottom:10px;}" +
                "p{color:#94a3b8;font-size:14px;max-width:400px;line-height:1.5;}" +
                ".pulse{width:60px;height:60px;border-radius:50%;border:3px solid #00d2ff;margin:20px 0;animation:pulse 1.5s infinite;}" +
                "@keyframes pulse{0%{transform:scale(0.8);opacity:0.3;}50%{transform:scale(1.2);opacity:1;}100%{transform:scale(0.8);opacity:0.3;}}" +
                ".input-box{margin-top:20px;display:flex;gap:10px;}" +
                "input{background:#151b24;border:1px solid #222d3d;color:#fff;padding:10px 14px;border-radius:8px;outline:none;font-size:14px;}" +
                "button{background:linear-gradient(135deg,#00d2ff,#38ef7d);color:#000;border:none;font-weight:bold;padding:10px 20px;border-radius:8px;cursor:pointer;}" +
                "</style></head><body>" +
                "<h1>SUO LINK</h1>" +
                "<div class='pulse'></div>" +
                "<p>Searching for CloudRedirect Host PC on Wi-Fi... (Make sure CloudRedirect is open on your PC)</p>" +
                "<div class='input-box'>" +
                "<input type='text' id='ip' placeholder='192.168.1.xxx:8585'>" +
                "<button onclick=\"connectManual()\">CONNECT</button>" +
                "</div>" +
                "<script>" +
                "function connectManual(){" +
                "  var val = document.getElementById('ip').value.trim();" +
                "  if(val){ if(!val.startsWith('http')) val = 'http://' + val; window.location.href = val; }" +
                "}" +
                "</script></body></html>";

        webView.loadDataWithBaseURL(null, html, "text/html", "UTF-8", null);
    }

    private void startBeaconDiscovery() {
        beaconListenerThread = new Thread(new BeaconRunnable(this));
        beaconListenerThread.setDaemon(true);
        beaconListenerThread.start();
    }

    public void loadHostUrl(String url) {
        prefs.edit().putString(PREF_KEY_LAST_HOST, url).apply();
        webView.loadUrl(url);
    }

    @Override
    public boolean dispatchGenericMotionEvent(MotionEvent event) {
        if ((event.getSource() & InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK &&
                event.getAction() == MotionEvent.ACTION_MOVE) {
            float lx = event.getAxisValue(MotionEvent.AXIS_X);
            float ly = event.getAxisValue(MotionEvent.AXIS_Y);
            float rx = event.getAxisValue(MotionEvent.AXIS_Z);
            float ry = event.getAxisValue(MotionEvent.AXIS_RZ);

            String js = String.format("if(window.sendInput){ window.sendInput({type:'stick_vector', x:%f, y:%f}); }", lx, ly);
            webView.post(new EvalJsRunnable(webView, js));
            return true;
        }
        return super.dispatchGenericMotionEvent(event);
    }

    @Override
    public boolean dispatchKeyEvent(KeyEvent event) {
        int action = event.getAction();
        boolean isDown = (action == KeyEvent.ACTION_DOWN);
        int keyCode = event.getKeyCode();

        String mappedKey = mapGamepadKeyCode(keyCode);
        if (mappedKey != null) {
            String type = isDown ? "key_down" : "key_up";
            String js = String.format("if(window.sendInput){ window.sendInput({type:'%s', key:'%s'}); }", type, mappedKey);
            webView.post(new EvalJsRunnable(webView, js));
            return true;
        }

        return super.dispatchKeyEvent(event);
    }

    private String mapGamepadKeyCode(int keyCode) {
        switch (keyCode) {
            case KeyEvent.KEYCODE_BUTTON_A: return "SPACE";
            case KeyEvent.KEYCODE_BUTTON_B: return "ESC";
            case KeyEvent.KEYCODE_BUTTON_X: return "E";
            case KeyEvent.KEYCODE_BUTTON_Y: return "TAB";
            case KeyEvent.KEYCODE_BUTTON_L1: return "SHIFT";
            case KeyEvent.KEYCODE_BUTTON_R1: return "CTRL";
            case KeyEvent.KEYCODE_DPAD_UP: return "UP";
            case KeyEvent.KEYCODE_DPAD_DOWN: return "DOWN";
            case KeyEvent.KEYCODE_DPAD_LEFT: return "LEFT";
            case KeyEvent.KEYCODE_DPAD_RIGHT: return "RIGHT";
            case KeyEvent.KEYCODE_BUTTON_START: return "ENTER";
            case KeyEvent.KEYCODE_BUTTON_SELECT: return "M";
            default: return null;
        }
    }

    private void setImmersiveMode() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.KITKAT) {
            getWindow().getDecorView().setSystemUiVisibility(
                    View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                            | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                            | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                            | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                            | View.SYSTEM_UI_FLAG_FULLSCREEN
                            | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY);
        }
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) setImmersiveMode();
    }

    @Override
    protected void onDestroy() {
        running = false;
        if (beaconListenerThread != null) beaconListenerThread.interrupt();
        super.onDestroy();
    }
}
