package com.cloudredirect.suolink;

import org.json.JSONObject;

import java.net.DatagramPacket;
import java.net.DatagramSocket;

public class BeaconRunnable implements Runnable {
    private static final int BEACON_PORT = 8586;
    private final MainActivity activity;

    public BeaconRunnable(MainActivity activity) {
        this.activity = activity;
    }

    @Override
    public void run() {
        DatagramSocket socket = null;
        try {
            socket = new DatagramSocket(BEACON_PORT);
            socket.setBroadcast(true);
            byte[] buf = new byte[2048];

            while (activity.isRunning()) {
                DatagramPacket packet = new DatagramPacket(buf, buf.length);
                socket.receive(packet);
                String msg = new String(packet.getData(), 0, packet.getLength(), "UTF-8");

                try {
                    JSONObject json = new JSONObject(msg);
                    if ("SUO_LINK_HOST".equals(json.optString("service"))) {
                        String name = json.optString("name", "CloudRedirect PC");
                        String ip = json.optString("ip");
                        int port = json.optInt("port", 8585);
                        String auth = json.optString("auth");
                        String tunnel = json.optString("tunnel", "");
                        String tailscale = json.optString("tailscale", "");
                        int verCode = json.optInt("versionCode", 1);
                        String ver = json.optString("version", "1.0.0");

                        activity.runOnUiThread(new OnBeaconDiscoveredRunnable(activity, name, ip, port, auth, tunnel, tailscale, verCode, ver));
                    }
                } catch (Exception ignored) { }
            }
        } catch (Exception ex) {
            // socket closed or error
        } finally {
            if (socket != null && !socket.isClosed()) socket.close();
        }
    }
}
