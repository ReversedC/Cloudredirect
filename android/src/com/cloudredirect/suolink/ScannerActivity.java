package com.cloudredirect.suolink;

import android.Manifest;
import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Rect;
import android.hardware.Camera;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.util.DisplayMetrics;
import android.view.Gravity;
import android.view.SurfaceHolder;
import android.view.SurfaceView;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;
import java.io.InputStream;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

@SuppressWarnings("deprecation")
public class ScannerActivity extends Activity implements SurfaceHolder.Callback, Camera.PreviewCallback, View.OnClickListener {

    public static final String EXTRA_SCANNED_URL = "scanned_url";
    private static final int REQUEST_CODE_PERMISSION_CAMERA = 701;
    private static final int REQUEST_CODE_PICK_GALLERY = 702;

    private static final int ID_BTN_GALLERY = 1001;
    private static final int ID_BTN_CANCEL = 1002;

    private Camera camera;
    private SurfaceView surfaceView;
    private SurfaceHolder surfaceHolder;
    private OverlayView overlayView;
    final AtomicBoolean isScanning = new AtomicBoolean(true);
    final AtomicBoolean isProcessingFrame = new AtomicBoolean(false);
    private ExecutorService decodeExecutor;
    int previewWidth = 0;
    int previewHeight = 0;
    private Vibrator vibrator;
    final Handler mainHandler = new Handler(Looper.getMainLooper());

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        requestWindowFeature(Window.FEATURE_NO_TITLE);
        getWindow().setFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN, WindowManager.LayoutParams.FLAG_FULLSCREEN);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        setImmersiveMode();

        decodeExecutor = Executors.newSingleThreadExecutor();
        vibrator = (Vibrator) getSystemService(Context.VIBRATOR_SERVICE);

        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(0xFF0B0E14);

        surfaceView = new SurfaceView(this);
        surfaceHolder = surfaceView.getHolder();
        surfaceHolder.addCallback(this);
        surfaceHolder.setType(SurfaceHolder.SURFACE_TYPE_PUSH_BUFFERS);
        root.addView(surfaceView, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        overlayView = new OverlayView(this);
        root.addView(overlayView, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        // Top Header
        LinearLayout topHeader = new LinearLayout(this);
        topHeader.setOrientation(LinearLayout.VERTICAL);
        topHeader.setGravity(Gravity.CENTER_HORIZONTAL);
        topHeader.setPadding(0, dpToPx(24), 0, 0);

        TextView title = new TextView(this);
        title.setText("📷 SCAN PC QR CODE");
        title.setTextColor(0xFF00D2FF);
        title.setTextSize(18);
        title.setTypeface(null, android.graphics.Typeface.BOLD);
        title.setGravity(Gravity.CENTER);
        topHeader.addView(title);

        TextView subtitle = new TextView(this);
        subtitle.setText("Point camera at the QR code on your PC screen");
        subtitle.setTextColor(0xFF94A3B8);
        subtitle.setTextSize(13);
        subtitle.setGravity(Gravity.CENTER);
        subtitle.setPadding(0, dpToPx(4), 0, 0);
        topHeader.addView(subtitle);

        FrameLayout.LayoutParams topParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.TOP);
        root.addView(topHeader, topParams);

        // Bottom Controls
        LinearLayout bottomBar = new LinearLayout(this);
        bottomBar.setOrientation(LinearLayout.HORIZONTAL);
        bottomBar.setGravity(Gravity.CENTER);
        bottomBar.setPadding(dpToPx(16), 0, dpToPx(16), dpToPx(24));

        Button btnGallery = new Button(this);
        btnGallery.setId(ID_BTN_GALLERY);
        btnGallery.setText("📁 SELECT FROM GALLERY");
        btnGallery.setTextColor(0xFFFFFFFF);
        btnGallery.setBackgroundColor(0xFF1E293B);
        btnGallery.setPadding(dpToPx(16), dpToPx(12), dpToPx(16), dpToPx(12));
        btnGallery.setOnClickListener(this);
        LinearLayout.LayoutParams btnGalleryParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        btnGalleryParams.setMargins(0, 0, dpToPx(16), 0);
        bottomBar.addView(btnGallery, btnGalleryParams);

        Button btnCancel = new Button(this);
        btnCancel.setId(ID_BTN_CANCEL);
        btnCancel.setText("✕ CANCEL");
        btnCancel.setTextColor(0xFF94A3B8);
        btnCancel.setBackgroundColor(0xFF151B24);
        btnCancel.setPadding(dpToPx(16), dpToPx(12), dpToPx(16), dpToPx(12));
        btnCancel.setOnClickListener(this);
        bottomBar.addView(btnCancel);

        FrameLayout.LayoutParams bottomParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM);
        root.addView(bottomBar, bottomParams);

        setContentView(root);
    }

    @Override
    public void onClick(View v) {
        if (v == null) return;
        int id = v.getId();
        if (id == ID_BTN_GALLERY) {
            openGalleryChooser();
        } else if (id == ID_BTN_CANCEL) {
            setResult(RESULT_CANCELED);
            finish();
        }
    }

    private int dpToPx(int dp) {
        DisplayMetrics dm = getResources().getDisplayMetrics();
        return Math.round(dp * (dm.xdpi / DisplayMetrics.DENSITY_DEFAULT));
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
    protected void onResume() {
        super.onResume();
        setImmersiveMode();
        isScanning.set(true);

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            if (checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
                requestPermissions(new String[]{Manifest.permission.CAMERA}, REQUEST_CODE_PERMISSION_CAMERA);
                return;
            }
        }

        startCameraSource();
    }

    @Override
    protected void onPause() {
        stopCameraSource();
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        if (decodeExecutor != null) {
            decodeExecutor.shutdown();
        }
        super.onDestroy();
    }

    private void startCameraSource() {
        if (camera != null) return;
        try {
            int numCameras = Camera.getNumberOfCameras();
            if (numCameras == 0) {
                Toast.makeText(this, "No camera found. Select QR from gallery.", Toast.LENGTH_LONG).show();
                openGalleryChooser();
                return;
            }

            int selectedCameraId = -1;
            Camera.CameraInfo info = new Camera.CameraInfo();
            for (int i = 0; i < numCameras; i++) {
                Camera.getCameraInfo(i, info);
                if (info.facing == Camera.CameraInfo.CAMERA_FACING_BACK) {
                    selectedCameraId = i;
                    break;
                }
            }
            if (selectedCameraId == -1) {
                selectedCameraId = 0;
            }

            camera = Camera.open(selectedCameraId);
            setupCameraParameters();
            if (surfaceHolder.getSurface() != null) {
                camera.setPreviewDisplay(surfaceHolder);
                camera.setOneShotPreviewCallback(this);
                camera.startPreview();
            }
        } catch (Exception ex) {
            Toast.makeText(this, "Cannot access camera: " + ex.getMessage(), Toast.LENGTH_SHORT).show();
            openGalleryChooser();
        }
    }

    private void stopCameraSource() {
        if (camera != null) {
            try {
                camera.setOneShotPreviewCallback(null);
                camera.stopPreview();
                camera.release();
            } catch (Exception ignored) {}
            camera = null;
        }
    }

    private void setupCameraParameters() {
        if (camera == null) return;
        try {
            Camera.Parameters params = camera.getParameters();

            List<String> focusModes = params.getSupportedFocusModes();
            if (focusModes != null) {
                if (focusModes.contains(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE)) {
                    params.setFocusMode(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE);
                } else if (focusModes.contains(Camera.Parameters.FOCUS_MODE_AUTO)) {
                    params.setFocusMode(Camera.Parameters.FOCUS_MODE_AUTO);
                }
            }

            List<Camera.Size> sizes = params.getSupportedPreviewSizes();
            if (sizes != null && !sizes.isEmpty()) {
                Camera.Size bestSize = sizes.get(0);
                int targetWidth = 1280;
                int targetHeight = 720;
                int minDiff = Integer.MAX_VALUE;
                for (Camera.Size s : sizes) {
                    int diff = Math.abs(s.width - targetWidth) + Math.abs(s.height - targetHeight);
                    if (diff < minDiff) {
                        minDiff = diff;
                        bestSize = s;
                    }
                }
                params.setPreviewSize(bestSize.width, bestSize.height);
                previewWidth = bestSize.width;
                previewHeight = bestSize.height;
            }

            camera.setParameters(params);
        } catch (Exception ignored) {}
    }

    @Override
    public void surfaceCreated(SurfaceHolder holder) {
        if (camera != null) {
            try {
                camera.setPreviewDisplay(holder);
                camera.setOneShotPreviewCallback(this);
                camera.startPreview();
            } catch (Exception ignored) {}
        }
    }

    @Override
    public void surfaceChanged(SurfaceHolder holder, int format, int width, int height) {
        if (surfaceHolder.getSurface() == null) return;
        try {
            if (camera != null) {
                camera.stopPreview();
                camera.setPreviewDisplay(surfaceHolder);
                camera.setOneShotPreviewCallback(this);
                camera.startPreview();
            }
        } catch (Exception ignored) {}
    }

    @Override
    public void surfaceDestroyed(SurfaceHolder holder) {
        stopCameraSource();
    }

    @Override
    public void onPreviewFrame(byte[] data, Camera cam) {
        if (!isScanning.get() || data == null) return;

        if (isProcessingFrame.compareAndSet(false, true)) {
            decodeExecutor.execute(new DecodeYuvTask(this, data, previewWidth, previewHeight));
        }
    }

    void onQrCodeRecognized(final String url) {
        if (!isScanning.compareAndSet(true, false)) return;
        mainHandler.post(new QrRecognizedTask(this, url));
    }

    void requestNextFrame() {
        if (isScanning.get() && camera != null) {
            try {
                camera.setOneShotPreviewCallback(this);
            } catch (Exception ignored) {}
        }
    }

    private void triggerHaptic() {
        if (vibrator != null) {
            try {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                    vibrator.vibrate(VibrationEffect.createOneShot(50, VibrationEffect.DEFAULT_AMPLITUDE));
                } else {
                    vibrator.vibrate(50);
                }
            } catch (Exception ignored) {}
        }
    }

    public void openGalleryChooser() {
        try {
            Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
            intent.setType("image/*");
            startActivityForResult(Intent.createChooser(intent, "Select QR Image"), REQUEST_CODE_PICK_GALLERY);
        } catch (Exception ex) {
            Toast.makeText(this, "Cannot open gallery: " + ex.getMessage(), Toast.LENGTH_SHORT).show();
        }
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == REQUEST_CODE_PERMISSION_CAMERA) {
            if (grantResults.length > 0 && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
                startCameraSource();
            } else {
                Toast.makeText(this, "Camera permission denied. Select QR from gallery.", Toast.LENGTH_LONG).show();
                openGalleryChooser();
            }
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);

        if (requestCode == REQUEST_CODE_PICK_GALLERY && resultCode == RESULT_OK && data != null) {
            Uri imageUri = data.getData();
            if (imageUri != null) {
                decodeExecutor.execute(new DecodeImageUriTask(this, imageUri));
            }
        }
    }

    static class DecodeYuvTask implements Runnable {
        private final ScannerActivity activity;
        private final byte[] data;
        private final int width;
        private final int height;

        public DecodeYuvTask(ScannerActivity activity, byte[] data, int width, int height) {
            this.activity = activity;
            this.data = data;
            this.width = width;
            this.height = height;
        }

        @Override
        public void run() {
            try {
                String result = QrDecoder.decodeYuv(data, width, height);
                if (result != null && !result.isEmpty() && activity.isScanning.get()) {
                    activity.onQrCodeRecognized(result);
                    return;
                }
            } catch (Throwable ignored) {
            } finally {
                activity.isProcessingFrame.set(false);
                activity.requestNextFrame();
            }
        }
    }

    static class QrRecognizedTask implements Runnable {
        private final ScannerActivity activity;
        private final String url;

        public QrRecognizedTask(ScannerActivity activity, String url) {
            this.activity = activity;
            this.url = url;
        }

        @Override
        public void run() {
            activity.triggerHaptic();
            Toast.makeText(activity, "✓ QR Code Scanned!", Toast.LENGTH_SHORT).show();

            Intent data = new Intent();
            data.putExtra(EXTRA_SCANNED_URL, url);
            activity.setResult(RESULT_OK, data);
            activity.finish();
        }
    }

    static class DecodeImageUriTask implements Runnable {
        private final ScannerActivity activity;
        private final Uri uri;

        public DecodeImageUriTask(ScannerActivity activity, Uri uri) {
            this.activity = activity;
            this.uri = uri;
        }

        @Override
        public void run() {
            try {
                InputStream is = activity.getContentResolver().openInputStream(uri);
                BitmapFactory.Options opts = new BitmapFactory.Options();
                opts.inJustDecodeBounds = true;
                BitmapFactory.decodeStream(is, null, opts);
                if (is != null) is.close();

                int maxDim = 1200;
                int scale = 1;
                while (opts.outWidth / scale > maxDim || opts.outHeight / scale > maxDim) {
                    scale *= 2;
                }

                BitmapFactory.Options decodeOpts = new BitmapFactory.Options();
                decodeOpts.inSampleSize = scale;
                is = activity.getContentResolver().openInputStream(uri);
                Bitmap bmp = BitmapFactory.decodeStream(is, null, decodeOpts);
                if (is != null) is.close();

                String result = QrDecoder.decodeBitmap(bmp);
                if (result != null && !result.isEmpty()) {
                    activity.onQrCodeRecognized(result);
                } else {
                    activity.mainHandler.post(new ShowToastTask(activity, "Could not find QR in photo. Try another image."));
                }
            } catch (Exception ex) {
                activity.mainHandler.post(new ShowToastTask(activity, "Error reading image: " + ex.getMessage()));
            }
        }
    }

    static class ShowToastTask implements Runnable {
        private final Activity activity;
        private final String msg;

        public ShowToastTask(Activity activity, String msg) {
            this.activity = activity;
            this.msg = msg;
        }

        @Override
        public void run() {
            Toast.makeText(activity, msg, Toast.LENGTH_LONG).show();
        }
    }

    // Custom Viewfinder Overlay with Steam Cyan Theme
    static class OverlayView extends View {
        private final Paint maskPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint framePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint linePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Rect scanBox = new Rect();
        private float scanLinePos = 0f;
        private boolean scanLineDown = true;

        public OverlayView(Context context) {
            super(context);
            maskPaint.setColor(0xB3000000); // 70% black mask

            framePaint.setColor(0xFF00D2FF); // Steam cyan
            framePaint.setStyle(Paint.Style.STROKE);
            framePaint.setStrokeWidth(6f);

            linePaint.setColor(0xFF38EF7D); // Neon green scanline
            linePaint.setStrokeWidth(4f);
        }

        @Override
        protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            int w = getWidth();
            int h = getHeight();
            if (w == 0 || h == 0) return;

            int boxSize = Math.min(w, h) * 3 / 5;
            if (boxSize < 240) boxSize = Math.min(240, Math.min(w, h));
            int left = (w - boxSize) / 2;
            int top = (h - boxSize) / 2;
            int right = left + boxSize;
            int bottom = top + boxSize;
            scanBox.set(left, top, right, bottom);

            // Draw dark mask around box
            canvas.drawRect(0, 0, w, top, maskPaint);
            canvas.drawRect(0, top, left, bottom, maskPaint);
            canvas.drawRect(right, top, w, bottom, maskPaint);
            canvas.drawRect(0, bottom, w, h, maskPaint);

            // Draw Corner Brackets (Steam aesthetic)
            int cl = boxSize / 6;
            // Top-Left
            canvas.drawLine(left, top, left + cl, top, framePaint);
            canvas.drawLine(left, top, left, top + cl, framePaint);
            // Top-Right
            canvas.drawLine(right, top, right - cl, top, framePaint);
            canvas.drawLine(right, top, right, top + cl, framePaint);
            // Bottom-Left
            canvas.drawLine(left, bottom, left + cl, bottom, framePaint);
            canvas.drawLine(left, bottom, left, bottom - cl, framePaint);
            // Bottom-Right
            canvas.drawLine(right, bottom, right - cl, bottom, framePaint);
            canvas.drawLine(right, bottom, right, bottom - cl, framePaint);

            // Animated pulsing scanline
            if (scanLineDown) {
                scanLinePos += 4f;
                if (scanLinePos >= boxSize - 10) scanLineDown = false;
            } else {
                scanLinePos -= 4f;
                if (scanLinePos <= 10) scanLineDown = true;
            }
            float y = top + scanLinePos;
            canvas.drawLine(left + 10, y, right - 10, y, linePaint);

            postInvalidateDelayed(16);
        }
    }
}
