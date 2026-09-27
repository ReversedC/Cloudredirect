package com.cloudredirect.suolink;

import android.graphics.Bitmap;
import android.graphics.Matrix;
import com.google.zxing.BarcodeFormat;
import com.google.zxing.BinaryBitmap;
import com.google.zxing.DecodeHintType;
import com.google.zxing.MultiFormatReader;
import com.google.zxing.PlanarYUVLuminanceSource;
import com.google.zxing.RGBLuminanceSource;
import com.google.zxing.Result;
import com.google.zxing.common.HybridBinarizer;
import java.util.EnumMap;
import java.util.EnumSet;
import java.util.Map;

public class QrDecoder {

    private static Map<DecodeHintType, Object> createHints() {
        Map<DecodeHintType, Object> hints = new EnumMap<>(DecodeHintType.class);
        hints.put(DecodeHintType.POSSIBLE_FORMATS, EnumSet.of(BarcodeFormat.QR_CODE));
        hints.put(DecodeHintType.TRY_HARDER, Boolean.TRUE);
        hints.put(DecodeHintType.CHARACTER_SET, "UTF-8");
        return hints;
    }

    public static String decodeBitmap(Bitmap bitmap) {
        if (bitmap == null) return null;

        // Try direct resolution
        String result = decodeBitmapInternal(bitmap);
        if (result != null) return result;

        // Try rotating 90 degrees if camera photo was in different orientation
        try {
            Matrix matrix = new Matrix();
            matrix.postRotate(90);
            Bitmap rotated = Bitmap.createBitmap(bitmap, 0, 0, bitmap.getWidth(), bitmap.getHeight(), matrix, true);
            result = decodeBitmapInternal(rotated);
            if (result != null) return result;

            matrix.postRotate(180);
            Bitmap rotated2 = Bitmap.createBitmap(bitmap, 0, 0, bitmap.getWidth(), bitmap.getHeight(), matrix, true);
            result = decodeBitmapInternal(rotated2);
            if (result != null) return result;
        } catch (Throwable ignored) {}

        return null;
    }

    private static String decodeBitmapInternal(Bitmap bitmap) {
        try {
            int width = bitmap.getWidth();
            int height = bitmap.getHeight();
            int[] pixels = new int[width * height];
            bitmap.getPixels(pixels, 0, width, 0, 0, width, height);

            RGBLuminanceSource source = new RGBLuminanceSource(width, height, pixels);
            BinaryBitmap binaryBitmap = new BinaryBitmap(new HybridBinarizer(source));

            MultiFormatReader reader = new MultiFormatReader();
            Result res = reader.decode(binaryBitmap, createHints());
            if (res != null && res.getText() != null && !res.getText().trim().isEmpty()) {
                return res.getText().trim();
            }
        } catch (Throwable ignored) {}

        return null;
    }

    public static String decodeYuv(byte[] yuvData, int width, int height) {
        if (yuvData == null || width <= 0 || height <= 0) return null;
        try {
            PlanarYUVLuminanceSource source = new PlanarYUVLuminanceSource(
                    yuvData, width, height, 0, 0, width, height, false);
            BinaryBitmap binaryBitmap = new BinaryBitmap(new HybridBinarizer(source));

            MultiFormatReader reader = new MultiFormatReader();
            Result res = reader.decode(binaryBitmap, createHints());
            if (res != null && res.getText() != null && !res.getText().trim().isEmpty()) {
                return res.getText().trim();
            }
        } catch (Throwable ignored) {}

        return null;
    }
}
