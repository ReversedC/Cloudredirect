package com.cloudredirect.gamehub;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.Environment;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;

import java.io.File;
import java.io.FileNotFoundException;

public class GameHubFileProvider extends ContentProvider {
    public static final String AUTHORITY = "com.cloudredirect.gamehub.fileprovider";

    public static Uri getUriForFile(Context context, File file) {
        return Uri.parse("content://" + AUTHORITY + "/" + file.getName());
    }

    @Override
    public boolean onCreate() {
        return true;
    }

    @Override
    public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        Context ctx = getContext();
        if (ctx == null) throw new FileNotFoundException("Context is null");
        String filename = uri.getLastPathSegment();
        if (filename == null) throw new FileNotFoundException("Invalid URI");

        File file = resolveFile(ctx, filename);
        if (file == null || !file.exists()) {
            throw new FileNotFoundException("File not found: " + filename);
        }

        return ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY);
    }

    @Override
    public String getType(Uri uri) {
        return "application/vnd.android.package-archive";
    }

    @Override
    public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) {
        Context ctx = getContext();
        if (ctx == null) return null;
        String filename = uri.getLastPathSegment();
        File file = resolveFile(ctx, filename);
        if (file == null || !file.exists()) return null;

        if (projection == null) {
            projection = new String[] {
                OpenableColumns.DISPLAY_NAME,
                OpenableColumns.SIZE
            };
        }

        MatrixCursor cursor = new MatrixCursor(projection, 1);
        MatrixCursor.RowBuilder row = cursor.newRow();
        for (String col : projection) {
            if (OpenableColumns.DISPLAY_NAME.equals(col)) {
                row.add(file.getName());
            } else if (OpenableColumns.SIZE.equals(col)) {
                row.add(file.length());
            } else {
                row.add(null);
            }
        }
        return cursor;
    }

    private static File resolveFile(Context ctx, String filename) {
        if (filename == null) return null;
        File file = new File(ctx.getCacheDir(), filename);
        if (file.exists()) return file;

        File ext = ctx.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS);
        if (ext != null) {
            file = new File(ext, filename);
            if (file.exists()) return file;
        }

        File ext2 = ctx.getExternalFilesDir(null);
        if (ext2 != null) {
            file = new File(ext2, filename);
            if (file.exists()) return file;
        }

        return null;
    }

    @Override
    public Uri insert(Uri uri, ContentValues values) { return null; }

    @Override
    public int delete(Uri uri, String selection, String[] selectionArgs) { return 0; }

    @Override
    public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) { return 0; }
}
