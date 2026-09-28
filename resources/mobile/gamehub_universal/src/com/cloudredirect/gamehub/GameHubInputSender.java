package com.cloudredirect.gamehub;

import android.content.Context;
import android.content.SharedPreferences;
import android.os.Handler;
import android.os.Looper;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class GameHubInputSender {
    public static final int UDP_PORT = 48999;
    private static final String KEY_HOST_IP = "host_pc_ip";

    private static GameHubInputSender instance;

    private final Context context;
    private final SharedPreferences prefs;
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private DatagramSocket udpSocket;
    private String hostIp;

    public interface DiscoveryCallback {
        void onDeviceFound(String hostName, String ipAddress);
        void onTimeout();
    }

    public static synchronized GameHubInputSender getInstance(Context context) {
        if (instance == null) {
            instance = new GameHubInputSender(context.getApplicationContext());
        }
        return instance;
    }

    private GameHubInputSender(Context context) {
        this.context = context;
        this.prefs = HudConfig.getPrefs(context);
        this.hostIp = prefs.getString(KEY_HOST_IP, "192.168.1.100");
        try {
            udpSocket = new DatagramSocket();
        } catch (Exception e) {
            e.printStackTrace();
        }
    }

    public String getHostIp() {
        return hostIp;
    }

    public void setHostIp(String ip) {
        this.hostIp = ip;
        prefs.edit().putString(KEY_HOST_IP, ip).apply();
    }

    public void sendButton(final HudConfig.ControlDef def, final boolean down) {
        executor.execute(new Runnable() {
            @Override
            public void run() {
                try {
                    String payload;
                    if (def.keyCode == HudConfig.KEYCODE_MOUSE_LEFT) {
                        payload = "M:" + (down ? "1" : "0") + ":1";
                    } else if (def.keyCode == HudConfig.KEYCODE_MOUSE_RIGHT) {
                        payload = "M:" + (down ? "1" : "0") + ":2";
                    } else if (def.keyCode > 0) {
                        payload = "K:" + (down ? "1" : "0") + ":" + def.keyCode + ":" + def.label;
                    } else {
                        // Gamepad button label fallback
                        payload = "PAD:" + (down ? "1" : "0") + ":" + def.label;
                    }
                    sendUdp(payload);
                } catch (Throwable t) {
                    t.printStackTrace();
                }
            }
        });
    }

    public void sendDpadDirection(final String direction, final boolean down) {
        executor.execute(new Runnable() {
            @Override
            public void run() {
                try {
                    sendUdp("DPAD:" + (down ? "1" : "0") + ":" + direction);
                } catch (Throwable t) {
                    t.printStackTrace();
                }
            }
        });
    }

    public void sendJoystick(final String stickId, final float x, final float y) {
        executor.execute(new Runnable() {
            @Override
            public void run() {
                try {
                    String prefix = "stick_right".equals(stickId) ? "JR:" : "J:";
                    String payload = prefix + String.format(java.util.Locale.US, "%.3f:%.3f", x, y);
                    sendUdp(payload);
                } catch (Throwable t) {
                    t.printStackTrace();
                }
            }
        });
    }

    public void sendPing(final Runnable onPong) {
        executor.execute(new Runnable() {
            @Override
            public void run() {
                try {
                    sendUdp("PING");
                    if (onPong != null) {
                        mainHandler.post(onPong);
                    }
                } catch (Throwable t) {
                    t.printStackTrace();
                }
            }
        });
    }

    private void sendUdp(String msg) {
        if (hostIp == null || hostIp.trim().isEmpty()) return;
        try {
            if (udpSocket == null || udpSocket.isClosed()) {
                udpSocket = new DatagramSocket();
            }
            byte[] data = msg.getBytes(StandardCharsets.UTF_8);
            InetAddress address = InetAddress.getByName(hostIp.trim());
            DatagramPacket packet = new DatagramPacket(data, data.length, address, UDP_PORT);
            udpSocket.send(packet);
        } catch (Throwable ignored) {}
    }

    public void startDiscovery(final DiscoveryCallback callback) {
        new Thread(new Runnable() {
            @Override
            public void run() {
                DatagramSocket scanSocket = null;
                try {
                    scanSocket = new DatagramSocket();
                    scanSocket.setBroadcast(true);
                    scanSocket.setSoTimeout(2500);

                    byte[] sendData = "DISCOVER_GAMEHUB".getBytes(StandardCharsets.UTF_8);
                    DatagramPacket sendPacket = new DatagramPacket(
                            sendData, sendData.length,
                            InetAddress.getByName("255.255.255.255"), UDP_PORT);
                    scanSocket.send(sendPacket);

                    byte[] recvBuf = new byte[1024];
                    DatagramPacket recvPacket = new DatagramPacket(recvBuf, recvBuf.length);
                    scanSocket.receive(recvPacket);

                    String response = new String(recvPacket.getData(), 0, recvPacket.getLength(), StandardCharsets.UTF_8);
                    final String pcIp = recvPacket.getAddress().getHostAddress();
                    final String pcName;
                    if (response.startsWith("GAMEHUB_PC_ACK|")) {
                        String[] parts = response.split("\\|");
                        pcName = parts.length > 1 ? parts[1] : "CloudRedirect PC";
                    } else {
                        pcName = "CloudRedirect PC";
                    }

                    mainHandler.post(new Runnable() {
                        @Override
                        public void run() {
                            setHostIp(pcIp);
                            if (callback != null) {
                                callback.onDeviceFound(pcName, pcIp);
                            }
                        }
                    });
                } catch (Exception e) {
                    mainHandler.post(new Runnable() {
                        @Override
                        public void run() {
                            if (callback != null) callback.onTimeout();
                        }
                    });
                } finally {
                    if (scanSocket != null && !scanSocket.isClosed()) {
                        scanSocket.close();
                    }
                }
            }
        }).start();
    }
}
