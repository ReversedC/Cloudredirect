package com.cloudredirect.suolink;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.net.Uri;
import java.io.InputStream;

public class ProcessGalleryQrRunnable implements Runnable {
    private final MainActivity activity;
    private final Uri uri;

    public ProcessGalleryQrRunnable(MainActivity activity, Uri uri) {
        this.activity = activity;
        this.uri = uri;
    }

    @Override
    public void run() {
        if (activity == null || uri == null) return;
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

            String scanned = QrDecoder.decodeBitmap(bmp);
            if (scanned != null && !scanned.isEmpty()) {
                activity.runOnUiThread(new ConnectSmartRunnable(activity, scanned));
            } else {
                activity.runOnUiThread(new ShowToastRunnable(activity, "Could not find QR code in image. Ensure PC screen is visible."));
            }
        } catch (Exception ex) {
            activity.runOnUiThread(new ShowToastRunnable(activity, "Failed to load image: " + ex.getMessage()));
        }
    }
}
