package com.cloudredirect.suolink;

public class OnBeaconDiscoveredRunnable implements Runnable {
    private final MainActivity activity;
    private final String name;
    private final String ip;
    private final int port;
    private final String auth;
    private final String tunnel;
    private final String tailscale;
    private final int verCode;
    private final String version;

    public OnBeaconDiscoveredRunnable(MainActivity activity, String name, String ip, int port, String auth, String tunnel, String tailscale, int verCode, String version) {
        this.activity = activity;
        this.name = name;
        this.ip = ip;
        this.port = port;
        this.auth = auth;
        this.tunnel = tunnel;
        this.tailscale = tailscale;
        this.verCode = verCode;
        this.version = version;
    }

    @Override
    public void run() {
        if (activity != null) {
            activity.onBeaconReceived(name, ip, port, auth, tunnel, tailscale, verCode, version);
        }
    }
}
