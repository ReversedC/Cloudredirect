package com.cloudredirect.suolink;

import android.app.DownloadManager;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.widget.Toast;

public class DownloadCompleteReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        String action = intent.getAction();
        if (DownloadManager.ACTION_DOWNLOAD_COMPLETE.equals(action)) {
            long downloadId = intent.getLongExtra(DownloadManager.EXTRA_DOWNLOAD_ID, -1);
            if (downloadId != -1) {
                DownloadManager dm = (DownloadManager) context.getSystemService(Context.DOWNLOAD_SERVICE);
                try {
                    Uri apkUri = dm.getUriForDownloadedFile(downloadId);
                    if (apkUri != null) {
                        Toast.makeText(context, "SUO Link update ready! Opening installer...", Toast.LENGTH_LONG).show();
                        Intent install = new Intent(Intent.ACTION_VIEW);
                        install.setDataAndType(apkUri, "application/vnd.android.package-archive");
                        install.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_GRANT_READ_URI_PERMISSION);
                        context.startActivity(install);
                    }
                } catch (Exception ex) {
                    Toast.makeText(context, "Update install error: " + ex.getMessage(), Toast.LENGTH_SHORT).show();
                }
            }
        }
    }
}
