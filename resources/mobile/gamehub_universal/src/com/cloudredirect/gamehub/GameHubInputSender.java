package com.cloudredirect.gamehub;

import android.content.Context;
import android.content.SharedPreferences;
import android.net.wifi.WifiManager;
import android.os.Handler;
import android.os.Looper;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.InterfaceAddress;
import java.net.NetworkInterface;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Enumeration;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class GameHubInputSender {
    public static final int UDP_PORT = 48999;
    public static final String KEY_HOST_IP = "host_pc_ip";
    public static final String KEY_LAST_PC_NAME = "host_pc_name";

    private static GameHubInputSender instance;

    private final Context context;
    private final SharedPreferences prefs;
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private DatagramSocket udpSocket;
    private String hostIp;
    private String lastPcName;
    private volatile boolean isHostConfirmed = false;
    private long lastDiscoveryAttempt = 0;

    public interface DiscoveryCallback {
        void onDeviceFound(String hostName, String ipAddress);
        void onTimeout();
    }

    public interface PingCallback {
        void onSuccess(int latencyMs);
        void onFailure(String error);
    }

    public interface ConnectionListener {
        void onConnectionUpdated(String ip, String pcName, boolean isOnline);
    }

    private final List<ConnectionListener> connectionListeners = new ArrayList<ConnectionListener>();

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
        this.lastPcName = prefs.getString(KEY_LAST_PC_NAME, "CloudRedirect PC");
        initSocketAndReceiver();
        startAutoDiscovery();
    }

    private void initSocketAndReceiver() {
        try {
            if (udpSocket == null || udpSocket.isClosed()) {
                udpSocket = new DatagramSocket();
                udpSocket.setBroadcast(true);
            }
        } catch (Exception e) {
            e.printStackTrace();
        }

        // Start background ACK receiver thread
        new Thread(new Runnable() {
            @Override
            public void run() {
                byte[] buf = new byte[1024];
                while (true) {
                    try {
                        if (udpSocket == null || udpSocket.isClosed()) {
                            try { Thread.sleep(500); } catch (Exception ignored) {}
                            continue;
                        }
                        DatagramPacket p = new DatagramPacket(buf, buf.length);
                        udpSocket.receive(p);
                        String msg = new String(p.getData(), 0, p.getLength(), StandardCharsets.UTF_8).trim();
                        if (msg.startsWith("GAMEHUB_PC_ACK|") || msg.equals("PONG")) {
                            final String pcIp = p.getAddress().getHostAddress();
                            String name = "CloudRedirect PC";
                            if (msg.startsWith("GAMEHUB_PC_ACK|")) {
                                String[] parts = msg.split("\\|");
                                if (parts.length > 1 && !parts[1].isEmpty()) name = parts[1];
                            }
                            final String finalName = name;
                            isHostConfirmed = true;
                            setHostIp(pcIp);
                            setLastPcName(finalName);
                            notifyConnectionUpdated(pcIp, finalName, true);
                        }
                    } catch (Throwable t) {
                        try { Thread.sleep(200); } catch (Exception ignored) {}
                    }
                }
            }
        }, "GameHubAckReceiver").start();
    }

    public synchronized void addConnectionListener(ConnectionListener listener) {
        if (listener != null && !connectionListeners.contains(listener)) {
            connectionListeners.add(listener);
            // Immediately notify with current state
            listener.onConnectionUpdated(hostIp, lastPcName, isHostConfirmed);
        }
    }

    public synchronized void removeConnectionListener(ConnectionListener listener) {
        connectionListeners.remove(listener);
    }

    private void notifyConnectionUpdated(final String ip, final String name, final boolean online) {
        mainHandler.post(new Runnable() {
            @Override
            public void run() {
                synchronized (GameHubInputSender.this) {
                    for (ConnectionListener l : connectionListeners) {
                        try {
                            l.onConnectionUpdated(ip, name, online);
                        } catch (Throwable ignored) {}
                    }
                }
            }
        });
    }

    public String getHostIp() {
        return hostIp;
    }

    public String getLastPcName() {
        return lastPcName;
    }

    public boolean isHostConfirmed() {
        return isHostConfirmed;
    }

    public void setHostIp(String ip) {
        this.hostIp = ip != null ? ip.trim() : "";
        prefs.edit().putString(KEY_HOST_IP, this.hostIp).apply();
    }

    public void setLastPcName(String name) {
        this.lastPcName = name;
        prefs.edit().putString(KEY_LAST_PC_NAME, name).apply();
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
                    } else if (def.keyCode == HudConfig.KEYCODE_MOUSE_MIDDLE) {
                        payload = "M:" + (down ? "1" : "0") + ":3";
                    } else if (def.keyCode > 0) {
                        payload = "K:" + (down ? "1" : "0") + ":" + def.keyCode + ":" + def.label;
                    } else {
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
                    String payload = prefix + String.format(Locale.US, "%.3f:%.3f", x, y);
                    sendUdp(payload);
                } catch (Throwable t) {
                    t.printStackTrace();
                }
            }
        });
    }

    public void sendText(final String text, final boolean pressEnter) {
        executor.execute(new Runnable() {
            @Override
            public void run() {
                try {
                    String prefix = pressEnter ? "TEXT_ENTER:" : "TEXT:";
                    sendUdp(prefix + text);
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

    public void testPing(final String targetIp, final PingCallback callback) {
        new Thread(new Runnable() {
            @Override
            public void run() {
                DatagramSocket socket = null;
                try {
                    socket = new DatagramSocket();
                    socket.setSoTimeout(2000);
                    long start = System.currentTimeMillis();
                    byte[] ping = "PING".getBytes(StandardCharsets.UTF_8);
                    InetAddress dest = InetAddress.getByName(targetIp.trim());
                    DatagramPacket packet = new DatagramPacket(ping, ping.length, dest, UDP_PORT);
                    socket.send(packet);

                    byte[] buf = new byte[256];
                    DatagramPacket recv = new DatagramPacket(buf, buf.length);
                    socket.receive(recv);
                    final int latency = (int) (System.currentTimeMillis() - start);

                    isHostConfirmed = true;
                    setHostIp(targetIp);
                    notifyConnectionUpdated(targetIp, lastPcName, true);

                    mainHandler.post(new Runnable() {
                        @Override
                        public void run() {
                            if (callback != null) callback.onSuccess(latency);
                        }
                    });
                } catch (final Exception e) {
                    mainHandler.post(new Runnable() {
                        @Override
                        public void run() {
                            if (callback != null) callback.onFailure(e.getMessage());
                        }
                    });
                } finally {
                    if (socket != null && !socket.isClosed()) socket.close();
                }
            }
        }).start();
    }

    private void sendUdp(String msg) {
        try {
            if (udpSocket == null || udpSocket.isClosed()) {
                udpSocket = new DatagramSocket();
                udpSocket.setBroadcast(true);
            }
            byte[] data = msg.getBytes(StandardCharsets.UTF_8);

            // 1. Send unicast to current host IP
            if (hostIp != null && !hostIp.trim().isEmpty()) {
                try {
                    InetAddress address = InetAddress.getByName(hostIp.trim());
                    DatagramPacket packet = new DatagramPacket(data, data.length, address, UDP_PORT);
                    udpSocket.send(packet);
                } catch (Throwable ignored) {}
            }

            // 2. If host IP is unconfirmed, ALSO broadcast to 255.255.255.255 and subnet
            // so CloudRedirect receives the touches with zero packet loss even if IP was wrong!
            if (!isHostConfirmed) {
                try {
                    DatagramPacket bcast = new DatagramPacket(data, data.length, InetAddress.getByName("255.255.255.255"), UDP_PORT);
                    udpSocket.send(bcast);
                } catch (Throwable ignored) {}

                sendSubnetBroadcast(data);

                // Quick background discovery trigger
                if (System.currentTimeMillis() - lastDiscoveryAttempt > 3000) {
                    lastDiscoveryAttempt = System.currentTimeMillis();
                    startAutoDiscovery();
                }
            }
        } catch (Throwable ignored) {}
    }

    private void sendSubnetBroadcast(byte[] data) {
        try {
            Enumeration<NetworkInterface> interfaces = NetworkInterface.getNetworkInterfaces();
            while (interfaces != null && interfaces.hasMoreElements()) {
                NetworkInterface ni = interfaces.nextElement();
                if (ni.isLoopback() || !ni.isUp()) continue;
                for (InterfaceAddress addr : ni.getInterfaceAddresses()) {
                    InetAddress broadcast = addr.getBroadcast();
                    if (broadcast != null) {
                        try {
                            DatagramPacket p = new DatagramPacket(data, data.length, broadcast, UDP_PORT);
                            udpSocket.send(p);
                        } catch (Throwable ignored) {}
                    }
                }
            }
        } catch (Throwable ignored) {}
    }

    public void startAutoDiscovery() {
        startDiscovery(null);
    }

    public void startDiscovery(final DiscoveryCallback callback) {
        new Thread(new Runnable() {
            @Override
            public void run() {
                WifiManager wifi = (WifiManager) context.getApplicationContext().getSystemService(Context.WIFI_SERVICE);
                WifiManager.MulticastLock lock = null;
                if (wifi != null) {
                    try {
                        lock = wifi.createMulticastLock("gamehub_discovery");
                        lock.setReferenceCounted(true);
                        lock.acquire();
                    } catch (Throwable ignored) {}
                }

                DatagramSocket scanSocket = null;
                try {
                    scanSocket = new DatagramSocket();
                    scanSocket.setBroadcast(true);
                    scanSocket.setSoTimeout(2500);

                    byte[] sendData = "DISCOVER_GAMEHUB".getBytes(StandardCharsets.UTF_8);

                    // 1. Send to 255.255.255.255
                    try {
                        scanSocket.send(new DatagramPacket(sendData, sendData.length, InetAddress.getByName("255.255.255.255"), UDP_PORT));
                    } catch (Exception ignored) {}

                    // 2. Broadcast on all network interface subnets
                    try {
                        Enumeration<NetworkInterface> interfaces = NetworkInterface.getNetworkInterfaces();
                        while (interfaces != null && interfaces.hasMoreElements()) {
                            NetworkInterface ni = interfaces.nextElement();
                            if (ni.isLoopback() || !ni.isUp()) continue;
                            for (InterfaceAddress addr : ni.getInterfaceAddresses()) {
                                InetAddress broadcast = addr.getBroadcast();
                                if (broadcast != null) {
                                    try {
                                        scanSocket.send(new DatagramPacket(sendData, sendData.length, broadcast, UDP_PORT));
                                    } catch (Exception ignored) {}
                                }
                            }
                        }
                    } catch (Exception ignored) {}

                    // 3. Send to current hostIp if configured
                    if (hostIp != null && !hostIp.trim().isEmpty()) {
                        try {
                            scanSocket.send(new DatagramPacket(sendData, sendData.length, InetAddress.getByName(hostIp.trim()), UDP_PORT));
                        } catch (Exception ignored) {}
                    }

                    // Await response
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

                    isHostConfirmed = true;
                    setHostIp(pcIp);
                    setLastPcName(pcName);
                    notifyConnectionUpdated(pcIp, pcName, true);

                    mainHandler.post(new Runnable() {
                        @Override
                        public void run() {
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
                    if (lock != null && lock.isHeld()) {
                        try { lock.release(); } catch (Throwable ignored) {}
                    }
                }
            }
        }).start();
    }
}
