﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.Webkit;
using Microsoft.ConnectedDevices;
using QuickShare.Droid.OnlineServiceHelpers;
using System.Threading;
using QuickShare.DevicesListManager;
using QuickShare.Droid.RomeComponent;
using Android.Database;
using Android.Provider;
using QuickShare.Droid.Classes;
using System.Threading.Tasks;
using QuickShare.Common;
using QuickShare.FileTransfer;
using Plugin.DeviceInfo;
using QuickShare.TextTransfer;
using Firebase.Iid;
using QuickShare.Droid.Classes.RevMob;
using Com.Revmob.Ads.Banner;
using Com.Revmob;
using System.Collections.Specialized;
using QuickShare.Common.Rome;
using System.IO;
using Com.Nononsenseapps.Filepicker;
using QuickShare.Droid.Classes.FilePicker;
using PCLStorage.Droid;
using PCLStorage;
using Android.Support.V4.Content;
using Android.Support.V4.App;
using Android.Content.PM;
using Android.Preferences;

namespace QuickShare.Droid.Activities
{
    [Activity(Icon = "@drawable/icon", Name = "com.ghiasi.quickshare.webviewcontainerpage")]
    public class WebViewContainerActivity : Activity
    {
        readonly string homeUrl = "file:///android_asset/html/home.html";
        readonly int PickImageId = 1000;
        readonly int PickFileId = 1001;
        readonly int SystemFilePickerId = 1002;
        readonly int SettingsId = 999;

        bool automaticRemoteSystemSelectionAllowed = true;
        int remoteSystemPrevCount = 0;

        WebView webView;

        SemaphoreSlim rsChangeSemaphore = new SemaphoreSlim(1, 1);
        SemaphoreSlim jsSendSemaphore = new SemaphoreSlim(1, 1);
        static bool IsInitialized = false;

        System.Timers.Timer finishLoadingTimer = null, checkClipboardTextTimer = null;
        Object finishLoadingLock = new Object();

        bool sendingFile = true;
        CancellationTokenSource sendFileCancellationTokenSource;

        string[] lastSelectedFiles;
        bool lastPreserveFolderStructure;

        bool isAskingForRomePermission = false;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.WebViewContainer);

            webView = FindViewById<WebView>(Resource.Id.webView);
            var client = new HybridWebViewClient(this);
            webView.SetWebViewClient(client);
            webView.Settings.JavaScriptEnabled = true;

            var settings = new Classes.Settings(this);
            InitUI(settings);

            checkClipboardTextTimer = new System.Timers.Timer()
            {
                Interval = 1000,
            };
            checkClipboardTextTimer.Elapsed += CheckClipboardTextTimer_Elapsed;
            checkClipboardTextTimer.Start();

            TryAskStorageReadWritePermissionOnStartup();

            if (IsInitialized)
            {
                Common.PackageManager.RemoteSystems.CollectionChanged += DevicesCollectionChanged;
                Common.RoamitCloudPackageManager.Devices.CollectionChanged += DevicesCollectionChanged;
                return;
            }
            IsInitialized = true;

            if (Common.PackageManager == null)
            {
                Common.PackageManager = new RomePackageManager(this);

                if (settings.UseInAppServiceOnWindowsDevices)
                    Common.PackageManager.SetAppServiceName("com.roamit.serviceinapp", "com.roamit.service");
                else
                    Common.PackageManager.SetAppServiceName("com.roamit.service");
            }
            else
            {
                foreach (var item in Common.PackageManager.RemoteSystems)
                {
                    Common.ListManager.AddDevice(item);
                }
            }

            if (Common.RoamitCloudPackageManager == null)
            {
                InitRoamitCloudPackageManager();
            }
            else
            {
                foreach (var item in Common.RoamitCloudPackageManager.Devices)
                {
                    Common.ListManager.AddDevice(item);
                }

                Common.RoamitCloudPackageManager.Devices.CollectionChanged -= DevicesCollectionChanged;
                Common.RoamitCloudPackageManager.Devices.CollectionChanged += DevicesCollectionChanged;
            }

            MigrateApiIfNecessary();
            CheckDevicesPermission();
            CreateNotificationChannel();

            if (Common.MessageCarrierPackageManager == null)
            {
                Common.MessageCarrierPackageManager = new RomePackageManager(this);
                Common.MessageCarrierPackageManager.SetAppServiceName("com.roamit.messagecarrierservice");
            }

            InitRomeDiscovery();

            Context context = this;
            Task.Run(async () =>
            {
#if DEBUG
                FirebaseInstanceId.Instance.DeleteInstanceId();
#endif
                await ServiceFunctions.RegisterDevice(context);
            });


            Analytics.TrackPage("WebViewContainerActivity");

            CheckForLegacyVersionInstallations();
        }

        private void CreateNotificationChannel()
        {
            // Create the NotificationChannel, but only on API 26+ because
            // the NotificationChannel class is new and not in the support library
            if (Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
            {
                var defaultChannel = new NotificationChannel(Classes.Notification.DefaultChannel, "Roamit", NotificationImportance.High)
                {
                    Description = "File receive notifications",
                };
                var progressChannel = new NotificationChannel(Classes.Notification.ProgressChannel, "Roamit file send progress notifications", NotificationImportance.Low)
                {
                    Description = "File send progress notifications",
                };
                var universalClipboardChannel = new NotificationChannel(Classes.Notification.UniversalClipboardChannel, "Roamit Universal Clipboard", NotificationImportance.Low)
                {
                    Description = "Universal clipboard notifications",
                };

                // Register the channel with the system
                var notificationManager = GetSystemService(Context.NotificationService) as NotificationManager;
                notificationManager.CreateNotificationChannel(defaultChannel);
                notificationManager.CreateNotificationChannel(progressChannel);
                notificationManager.CreateNotificationChannel(universalClipboardChannel);
            }
        }

        private async void TryAskStorageReadWritePermissionOnStartup()
        {
            do
            {
                await Task.Delay(1000);
            }
            while (isAskingForRomePermission);

            await TryAskStorageReadWritePermission();
        }

        private async void CheckDevicesPermission()
        {
            if (CloudServiceAuthenticationHelper.IsAuthenticatedForApiV3())
            {
                var apiLoginInfo = (CloudServiceAuthenticationHelper.GetApiLoginInfo());
                var user = new QuickShare.Common.Service.v3.User(apiLoginInfo.AccountId, apiLoginInfo.Token);
                var hasPermission = await user.HasDevicesPermission();

                if (!hasPermission)
                {
                    ShowLoginWarning();
                }
            }
        }

        private async void MigrateApiIfNecessary()
        {
            if (CloudServiceAuthenticationHelper.IsAuthenticatedForApiV3())
                return;

            try
            {
                await CloudServiceAuthenticationHelper.MigrateFromV1ToV3();
                InitRoamitCloudPackageManager();
                Analytics.TrackEvent("ApiMigration", "V1toV3", "Success");
                CheckDevicesPermission();
            }
            catch (Exception ex)
            {
                Analytics.TrackEvent("ApiMigration", "V1toV3", $"Failed:{ex.Message}");
                System.Diagnostics.Debug.WriteLine($"API migration from v1 to v3 failed: {ex.ToString()}");
            }
        }

        private void ShowLoginWarning()
        {
            SendJavascriptToWebView($"showLoginWarning();");
        }

        private void HideLoginWarning()
        {
            SendJavascriptToWebView($"hideLoginWarning();");
        }

        private void InitUI(Classes.Settings settings)
        {
            if (IsShareDialog)
            {
                InitShareDialog(settings.Theme);
            }
            else
            {
                if (settings.Theme == AppTheme.Dark)
                    webView.LoadUrl($"{homeUrl}#dark");
                else
                    webView.LoadUrl($"{homeUrl}#light");

                ShowWhatsNewIfNecessary();
            }

            SetNavBarColor(settings.Theme);
        }

        private void SetNavBarColor(AppTheme theme)
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.Lollipop)
                return;

            Window.SetNavigationBarColor(Android.Graphics.Color.Black);
        }

        private async void InitRoamitCloudPackageManager()
        {
            Common.RoamitCloudPackageManager = new RoamitCloudPackageManager();

            if (CloudServiceAuthenticationHelper.IsAuthenticatedForApiV3())
                Common.RoamitCloudPackageManager.SetLoginInfoV3(CloudServiceAuthenticationHelper.GetApiLoginInfo());
            else
                Common.RoamitCloudPackageManager.SetLoginInfoV1(await MSAAuthenticator.GetUserUniqueIdAsync());

            Common.RoamitCloudPackageManager.Devices.CollectionChanged += DevicesCollectionChanged;

            Common.RoamitCloudPackageManager.BeginDiscoverDevices(ServiceFunctions.GetDeviceUniqueId());
        }

        private void ShowWhatsNewIfNecessary()
        {
            WhatsNew whatsNew = new WhatsNew(this);

            if (!whatsNew.ShouldShowWhatsNew)
                return;

            var alert = new AlertDialog.Builder(this)
                    .SetTitle(whatsNew.GetTitle())
                    .SetMessage(whatsNew.GetText())
                    .SetPositiveButton("Got it", (s, e) => { });

            RunOnUiThread(() =>
            {
                alert.Show();
            });

            whatsNew.Shown();
        }

        private async void CheckForLegacyVersionInstallations()
        {
            bool installed = PackageManager.GetInstalledPackages(Android.Content.PM.PackageInfoFlags.MatchAll).FirstOrDefault(x => x.PackageName == "com.ghiasi.roamit") != null;

            if (installed)
            {
                while ((Common.ListManager.RemoteSystems.Count == 0) && (Common.ListManager.SelectedRemoteSystem == null))
                    await Task.Delay(500);

                var alert = new AlertDialog.Builder(this)
                    .SetTitle("Please uninstall the legacy version of Roamit.")
                    .SetMessage("Having both versions side-by-side might cause problems.\n" +
                    "Uninstall the previous version to have the best experience possible.")
                    .SetPositiveButton("OK", (s, e) => { });

                RunOnUiThread(() =>
                {
                    alert.Show();
                });
            }
        }

        private bool IsShareDialog { get => ((Intent.Action == Intent.ActionSend) || (Intent.Action == Intent.ActionSendMultiple)); }

        private void InitShareDialog(AppTheme theme)
        {
            string sharePage = (theme == AppTheme.Light) ? "share" : "shar2";
            if ((Intent.Action == Intent.ActionSend) && (Intent.Extras.ContainsKey(Intent.ExtraStream)))
            {
                var uri = (Android.Net.Uri)Intent.Extras.GetParcelable(Intent.ExtraStream);
                var fileUrl = FilePathHelper.GetPath(this, uri);

                Common.ShareFiles = new string[] { fileUrl };

                webView.LoadUrl($"{homeUrl}#{sharePage}file");
            }
            else if (Intent.Action == Intent.ActionSendMultiple && Intent.Extras.ContainsKey(Intent.ExtraStream))
            {
                string[] urls = Intent.Extras.GetParcelableArrayList(Intent.ExtraStream)
                    .Cast<Android.Net.Uri>()
                    .Select(x => FilePathHelper.GetPath(this, x))
                    .ToArray();

                Common.ShareFiles = urls;

                webView.LoadUrl($"{homeUrl}#{sharePage}file");
            }
            else if ((Intent.Action == Intent.ActionSend) && (Intent.Type == "text/plain"))
            {
                string sharedText = Intent.GetStringExtra(Intent.ExtraText);

                Common.ShareFiles = null;
                Common.ShareText = sharedText;

                bool isValidUri = System.Uri.TryCreate(sharedText, UriKind.Absolute, out _);
                if (isValidUri)
                    webView.LoadUrl($"{homeUrl}#{sharePage}link");
                else
                    webView.LoadUrl($"{homeUrl}#{sharePage}clipboard");
            }
            else
            {
                webView.LoadUrl($"{homeUrl}#{sharePage}");
            }
        }

        private void CheckClipboardTextTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            SetClipboardPreviewText();
        }

        private void WebViewBack()
        {
            SendJavascriptToWebView("window.history.back();");
        }

        public override void OnBackPressed()
        {
            if ((!IsHomeUrl(webView.Url)) && (!IsShareDialog))
            {
                if (sendingFile)
                {
                    if (sendFileCancellationTokenSource == null)
                        sendingFile = false;
                    else
                        sendFileCancellationTokenSource.Cancel();
                }

                WebViewBack();
            }
            else
            {
                base.OnBackPressed();
            }
        }

        private bool IsHomeUrl(string url)
        {
            return ((url == homeUrl) || (url == $"{homeUrl}#dark") || (url == $"{homeUrl}#light"));
        }

        private async void SendJavascriptToWebView(string jsContent)
        {
            await jsSendSemaphore.WaitAsync();
            try
            {
                webView.EvaluateJavascript(jsContent, null);
                await Task.Delay(20);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"*** ERROR IN SendJavascriptToWebView ({jsContent}): {ex.Message}. Will try loadUrl instead.");

                try
                {
                    webView.LoadUrl($"javascript:{jsContent}");
                    await Task.Delay(20);
                }
                catch (Exception ex2)
                {
                    System.Diagnostics.Debug.WriteLine($"*** ERROR #2 IN SendJavascriptToWebView ({jsContent}): {ex2.Message}. Sending javascript command failed.");
                }
            }
            finally
            {
                jsSendSemaphore.Release();
            }
        }

        private async void InitRomeDiscovery()
        {
            Common.PackageManager.RemoteSystems.CollectionChanged += DevicesCollectionChanged;
            Platform.FetchAuthCode += Platform_FetchAuthCode;

            await Common.PackageManager.InitializeDiscovery();
            await Common.MessageCarrierPackageManager.InitializeDiscovery();
        }


        private async void DevicesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            await rsChangeSemaphore.WaitAsync();

            try
            {
                RunOnUiThread(() =>
                {
                    bool isRome = false;

                    var normalizer = new RemoteSystemNormalizer();
                    if (e.NewItems != null)
                        foreach (var item in e.NewItems)
                        {
                            var nrs = normalizer.Normalize(item);
                            if ((nrs.Type == DeviceType.Windows) && 
                                (Common.ListManager.RemoteSystems
                                    .Concat(Common.ListManager.SelectedRemoteSystem == null ? new NormalizedRemoteSystem[] { } : new[] { Common.ListManager.SelectedRemoteSystem })
                                    .Any(x => x.Id != nrs.Id && x.DisplayName == nrs.DisplayName && x.Type == DeviceType.GraphWindowsDevice)))
                            {
                                if (nrs.IsAvailableByProximity)
                                {
                                    // We already have this device via cloud, but it's now available by proximity. So we'll replace the cloud one with proximity one
                                    Common.ListManager.RemoveDeviceByName(nrs.DisplayName);
                                    Common.ListManager.AddDevice(item);
                                    UpdateRemoteSystemIdInList(nrs);
                                }
                            }
                            else 
                            {
                                Common.ListManager.AddDevice(item);
                                AddRemoteSystemToList(nrs);
                            }

                            if (nrs.Type == DeviceType.Windows)
                                isRome = true;
                        }

                    if (e.OldItems != null)
                        foreach (var item in e.OldItems)
                        {
                            Common.ListManager.RemoveDevice(item);
                            var nrs = normalizer.Normalize(item);
                            RemoveRemoteSystemFromList(nrs);
                        }

                    SelectItemIfNecessary(isRome);

                    if (((Common.ListManager.RemoteSystems.Count > 0) || (Common.ListManager.SelectedRemoteSystem != null)) && (isRome))
                    {
                        AuthenticateDialog.Hide();
                    }

                    try
                    {
                        lock (finishLoadingLock)
                        {
                            if (finishLoadingTimer?.Enabled == true)
                            {
                                System.Diagnostics.Debug.WriteLine("Timer stopped!");
                                finishLoadingTimer.Stop();
                                finishLoadingTimer = null;
                            }

                            if (finishLoadingTimer == null)
                            {
                                finishLoadingTimer = new System.Timers.Timer()
                                {
                                    AutoReset = false,
                                    Interval = 5000,
                                };
                                finishLoadingTimer.Elapsed += FinishLoadingTimer_Elapsed;
                            }

                            if (isRome)
                            {
                                finishLoadingTimer.Start();
                                System.Diagnostics.Debug.WriteLine("Timer started...");
                            }
                        }
                        
                    }
                    catch { }

                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.ToString());
            }
            finally
            {
                rsChangeSemaphore.Release();
            }
        }

        private void RemoveRemoteSystemFromList(NormalizedRemoteSystem nrs)
        {
            SendJavascriptToWebView($"removeItem('{nrs.Id.NormalizeForJsCall()}');");
        }

        private void AddRemoteSystemToList(NormalizedRemoteSystem nrs)
        {
            SendJavascriptToWebView($"addItem('{nrs.DisplayName.NormalizeForJsCall()}', '{TranslateDeviceKindToWebViewFormat(nrs.Kind).NormalizeForJsCall()}', '{nrs.Id.NormalizeForJsCall()}');");
        }

        private void UpdateRemoteSystemIdInList(NormalizedRemoteSystem nrs)
        {
            SendJavascriptToWebView($"updateItemId('{nrs.DisplayName.NormalizeForJsCall()}', '{nrs.Id.NormalizeForJsCall()}');"); //TODO
        }

        private void SelectItemIfNecessary(bool shouldBlockAutomaticSelection)
        {
            if ((Common.ListManager.RemoteSystems.Count > 0) &&
                (((automaticRemoteSystemSelectionAllowed) /*&& (Common.ListManager.RemoteSystems.Count > remoteSystemPrevCount)*/) || (Common.ListManager.SelectedRemoteSystem == null)))
            {
                remoteSystemPrevCount = Common.ListManager.RemoteSystems.Count;
                Common.ListManager.SelectHighScoreItem();
                var s = $"selectItem('{Common.ListManager.SelectedRemoteSystem?.Id?.NormalizeForJsCall()}');";
                SendJavascriptToWebView(s);

                //Only block automatic remote system selection when it's from Rome. (Windows devices should have more priority)
                if (shouldBlockAutomaticSelection)
                    BlockAutomaticRemoteSystemSelection();
            }
        }

        private async void BlockAutomaticRemoteSystemSelection()
        {
            if (!automaticRemoteSystemSelectionAllowed)
                return;

            await Task.Delay(TimeSpan.FromSeconds(1));
            automaticRemoteSystemSelectionAllowed = false;
        }

        private void FinishLoadingTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Timer has done its job :]");
            finishLoadingTimer.Enabled = false;
            FinishedLoadingDevices();
        }

        private async void FinishedLoadingDevices()
        {
            var Ids = Common.PackageManager.RemoteSystems.Where(x => x.Kind.Value != RemoteSystemKinds.Unknown.Value).Select(x => x.Id);
            await ServiceFunctions.RegisterWinDeviceIds(Ids);
        }

        public string TranslateDeviceKindToWebViewFormat(string kind)
        {
            switch (kind.ToLower())
            {
                case "xbox":
                    return "videogame_asset";
                case "mobile":
                case "phone":
                    return "smartphone";
                case "android":
                    return "android";
                case "unknown":
                default:
                    return "laptop";
            }
        }

        private void Platform_FetchAuthCode(string oauthUrl)
        {
            RunOnUiThread(async () =>
            {
                isAskingForRomePermission = true;
                System.Diagnostics.Debug.WriteLine(oauthUrl);
                var result = await AuthenticateDialog.ShowAsync(this, MsaAuthPurpose.ProjectRomePlatform, oauthUrl);
                isAskingForRomePermission = false;
            });
        }

        private void SetClipboardPreviewText()
        {
            try
            {
                RunOnUiThread(() =>
                {
                    string content = ClipboardHelper.GetClipboardText(this);

                    var contentPreview = content;

                    //truncate text preview if it's too long
                    if (contentPreview.Length > 61)
                        contentPreview = contentPreview.Substring(0, 60) + "...";

                    // remove newlines
                    contentPreview = contentPreview.Replace("\r", " ").Replace("\n", " ");

                    // remove multiple spaces
                    while (contentPreview.Contains("  "))
                        contentPreview = contentPreview.Replace("  ", " ");

                    SendJavascriptToWebView($"setClipboardText('{contentPreview.NormalizeForJsCall()}');");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Can't read clipboard: " + ex.Message);
            }
        }

        private async void SelectDevice(string id)
        {
            if ((Common.ListManager.SelectedRemoteSystem != null) && (Common.ListManager.SelectedRemoteSystem.Id == id))
                return;

            var item = Common.ListManager.RemoteSystems.FirstOrDefault(x => x.Id == id);

            if (item == null)
            {
                await Task.Delay(1000);

                item = Common.ListManager.RemoteSystems.FirstOrDefault(x => x.Id == id);
                if (item == null)
                {
                    Common.ListManager.SelectedRemoteSystem = null;
                    SelectItemIfNecessary(true);

                    return;
                }
            }

            Common.ListManager.Select(item);
        }

        #region Send

        private void ShowProgress()
        {
            SendJavascriptToWebView("showProgress();");
            Analytics.TrackPage("Send");
        }

        private void SetProgressText(string text)
        {
            SendJavascriptToWebView($"setProgressText('{text.NormalizeForJsCall()}');");
        }

        private void SetProgressSubtext(string text)
        {
            SendJavascriptToWebView($"setProgressSubtext('{text.NormalizeForJsCall()}');");
        }

        private void ShowProgressButtons()
        {
            SendJavascriptToWebView($"showProgressButtons();");
        }

        private void SetProgressValue(double val, double max)
        {
            var d = val / max;
            SendJavascriptToWebView($"setProgressValue({d});");
        }

        private void SetProgressValueToIndetermine()
        {
            SetProgressValue(-1.0, 1.0);
        }

        private string GetRealPathFromURI(Android.Net.Uri contentURI)
        {
            ICursor cursor = ContentResolver.Query(contentURI, null, null, null, null);
            cursor.MoveToFirst();
            string documentId = cursor.GetString(0);
            documentId = documentId.Split(':')[1];
            cursor.Close();

            cursor = ContentResolver.Query(
            Android.Provider.MediaStore.Images.Media.ExternalContentUri,
            null, MediaStore.Images.Media.InterfaceConsts.Id + " = ? ", new[] { documentId }, null);
            cursor.MoveToFirst();
            string path = cursor.GetString(cursor.GetColumnIndex(MediaStore.Images.Media.InterfaceConsts.Data));
            cursor.Close();

            return path;
        }

        protected override async void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            if (requestCode == PickImageId)
            {
                if ((resultCode == Result.Ok) && (data != null))
                {
                    List<string> files = new List<string>();

                    ClipData clipData = data.ClipData;
                    if (clipData != null)
                    {
                        for (int i = 0; i < clipData.ItemCount; i++)
                        {
                            ClipData.Item item = clipData.GetItemAt(i);
                            var uri = item.Uri;
                            files.Add(FilePathHelper.GetPath(this, uri));
                        }
                    }
                    else
                    {
                        Android.Net.Uri uri = data.Data;
                        var file = FilePathHelper.GetPath(this, uri);
                        files.Add(file);
                    }

                    if (files.Count == 0)
                    {
                        return;
                    }

                    await SendFiles(files.ToArray(), false);
                }
                else
                {

                }
            }
            else if (requestCode == PickFileId)
            {
                if ((resultCode == Result.Ok) && (data != null))
                {
                    var files = Utils.GetSelectedFilesFromResult(data).Select(x => Utils.GetFileForUri(x).AbsolutePath).ToArray();

                    await SendFiles(files, true);
                }
            }
            else if (requestCode == SystemFilePickerId)
            {
                if ((resultCode == Result.Ok) && (data != null))
                {
                    List<string> files = new List<string>();

                    try
                    {
                        ClipData clipData = data.ClipData;
                        if (clipData != null)
                        {
                            for (int i = 0; i < clipData.ItemCount; i++)
                            {
                                ClipData.Item item = clipData.GetItemAt(i);
                                var uri = item.Uri;
                                files.Add(FilePathHelper.GetPath(this, uri));
                            }
                        }
                        else
                        {
                            Android.Net.Uri uri = data.Data;
                            var file = FilePathHelper.GetPath(this, uri);
                            files.Add(file);
                        }

                        //Make sure files are valid
                        files.Select(x => new Java.IO.File(x)).ToList();

                        if (files.Count == 0)
                        {
                            return;
                        }
                    }
                    catch (NonPrimaryExternalStorageNotSupportedException)
                    {
                        var alert = new AlertDialog.Builder(this)
                            .SetTitle("Selecting from SD Card is not currently supported.")
                            .SetMessage("This will be added in a future version.")
                            .SetPositiveButton("Ok", (s, e) => { });

                        RunOnUiThread(() =>
                        {
                            alert.Show();
                        });

                        return;
                    }
                    catch (Exception ex)
                    {
                        var alert = new AlertDialog.Builder(this)
                            .SetTitle("Can't send selected files.")
                            .SetMessage("Roamit can't access some of the selected files, or their provider is not supported.")
                            .SetPositiveButton("Ok", (s, e) => { });

                        RunOnUiThread(() =>
                        {
                            alert.Show();
                        });

                        return;
                    }

                    await SendFiles(files.ToArray(), false);
                }
            }
            else if (requestCode == SettingsId)
            {
                var settings = new Classes.Settings(this);
                InitUI(settings);
            }

            base.OnActivityResult(requestCode, resultCode, data);
        }

        private async Task PickAndSendPicture()
        {
            if (!await TryAskStorageReadPermission())
                return;


            Intent getIntent = new Intent(Intent.ActionGetContent);
            getIntent.SetType("image/*,video/*");
            getIntent.PutExtra(Intent.ExtraAllowMultiple, true);
            getIntent.PutExtra(Intent.ExtraLocalOnly, true);

            Intent pickIntent = new Intent(Intent.ActionPick, Android.Provider.MediaStore.Images.Media.ExternalContentUri);
            pickIntent.SetType("image/*,video/*");
            pickIntent.PutExtra(Intent.ExtraAllowMultiple, true);
            pickIntent.PutExtra(Intent.ExtraLocalOnly, true);

            Intent chooserIntent = Intent.CreateChooser(getIntent, "Select Picture");
            chooserIntent.PutExtra(Intent.ExtraInitialIntents, new Intent[] { pickIntent });

            StartActivityForResult(chooserIntent, PickImageId);
        }

        private async Task PickAndSendFile()
        {
            if (!await TryAskStorageReadPermission())
                return;

            var settings = new Classes.Settings(this);

            if (settings.UseSystemFilePicker)
            {
                Intent i = new Intent(Intent.ActionOpenDocument);
                i.AddCategory(Intent.CategoryDefault);
                i.PutExtra("android.content.extra.SHOW_ADVANCED", true);
                i.PutExtra("android.content.extra.FANCY", true);
                i.PutExtra("android.content.extra.SHOW_FILESIZE", true);
                i.PutExtra(Intent.ExtraAllowMultiple, true);
                i.SetType("*/*");

                StartActivityForResult(Intent.CreateChooser(i, "Select Files"), SystemFilePickerId);
            }
            else
            {
                Intent i = new Intent(this, settings.Theme == AppTheme.Dark ? typeof(BackHandlingFilePickerActivityDark) : typeof(BackHandlingFilePickerActivityLight));

                i.PutExtra(FilePickerActivity.ExtraAllowMultiple, true);
                i.PutExtra(FilePickerActivity.ExtraAllowCreateDir, false);
                i.PutExtra(FilePickerActivity.ExtraMode, FilePickerActivity.ModeFileAndDir);

                i.PutExtra(FilePickerActivity.ExtraStartPath, Android.OS.Environment.ExternalStorageDirectory.AbsolutePath);

                StartActivityForResult(i, PickFileId);
            }
        }

        private async Task RetrySendFiles()
        {
            await SendFiles(lastSelectedFiles, lastPreserveFolderStructure);
        }

        private async Task SendFiles(string[] files, bool preserveFolderStructure)
        {
            ShowProgress();

            if (!await TryAskStorageReadPermission())
            {
                WebViewBack();
                return;
            }

            lastSelectedFiles = files;
            lastPreserveFolderStructure = preserveFolderStructure;

            if (IsCloudCommunication())
                await SendFilesToCloudDevice(files, preserveFolderStructure);
            else
                await SendFilesToRomeLocalDevice(files, preserveFolderStructure);
        }

        TaskCompletionSource<bool> permissionAskResultTcs;
        private async Task<bool> TryAskStorageReadPermission()
        {
            if (ContextCompat.CheckSelfPermission(this, Android.Manifest.Permission.ReadExternalStorage) != Android.Content.PM.Permission.Granted)
            {
                permissionAskResultTcs = new TaskCompletionSource<bool>();
                ActivityCompat.RequestPermissions(this, new[] { Android.Manifest.Permission.ReadExternalStorage }, 101);

                var permissionAskResult = await permissionAskResultTcs.Task;

                if (permissionAskResult == false && 
                    !ActivityCompat.ShouldShowRequestPermissionRationale(this, Android.Manifest.Permission.ReadExternalStorage)) {

                    // User has selected "Don't ask again", so we show them a message instead.

                    var alert = new AlertDialog.Builder(this)
                            .SetTitle("Permission needed")
                            .SetMessage("We need your permission in order to be able to send files. Please grant Storage permission to Roamit in system settings.")
                            .SetPositiveButton("Ok", (s, e) => { });

                    RunOnUiThread(() =>
                    {
                        alert.Show();
                    });
                }

                return permissionAskResult;
            }
            return true;
        }

        private async Task<bool> TryAskStorageReadWritePermission()
        {
            if (ContextCompat.CheckSelfPermission(this, Android.Manifest.Permission.ReadExternalStorage) != Android.Content.PM.Permission.Granted ||
                ContextCompat.CheckSelfPermission(this, Android.Manifest.Permission.WriteExternalStorage) != Android.Content.PM.Permission.Granted)
            {
                permissionAskResultTcs = new TaskCompletionSource<bool>();
                ActivityCompat.RequestPermissions(this, new[] {
                    Android.Manifest.Permission.ReadExternalStorage,
                    Android.Manifest.Permission.WriteExternalStorage,
                }, 102);

                var permissionAskResult = await permissionAskResultTcs.Task;

                if (permissionAskResult == false &&
                    !ActivityCompat.ShouldShowRequestPermissionRationale(this, Android.Manifest.Permission.ReadExternalStorage)) {

                    // User has selected "Don't ask again", so we show them a message instead.

                    var alert = new AlertDialog.Builder(this)
                            .SetTitle("Permission needed")
                            .SetMessage("We need your permission in order to be able to send and receive files. Please grant Storage permission to Roamit in system settings.")
                            .SetPositiveButton("Ok", (s, e) => { });

                    RunOnUiThread(() =>
                    {
                        alert.Show();
                    });
                }

                return permissionAskResult;
            }
            return true;
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
        {
            if (requestCode == 101 || requestCode == 102)
            {
                if (grantResults.Length > 0)
                {
                    permissionAskResultTcs.TrySetResult(grantResults.Where(x => x != Permission.Granted).Count() == 0);
                }
            }
        }

        private async Task SendFilesToCloudDevice(string[] files, bool preserveFolderStructure)
        {
            var rs = Common.GetCurrentNormalizedRemoteSystem();

            SetProgressText("Connecting...");
            Common.RoamitCloudPackageManager.SetRemoteDevice(rs.Id);

            if (rs.Type == DeviceType.GraphWindowsDevice)
            {
                await Common.RoamitCloudPackageManager.LaunchUri(new Uri("roamit://receiveDialog"), rs);
            }

            var transferResult = await BeginSend(files, Common.RoamitCloudPackageManager, preserveFolderStructure, sendFinishService: true);

            FinishSend(transferResult, isLocalRome: false);
        }

        private void FinishSend(FileTransferResult transferResult, bool isLocalRome)
        {
            string message = transferResult.ToString(); //TODO
            string sendEventText = isLocalRome ? "SendToWindows" : (IsAndroidDevice() ? "SendToAndroid" : "SendToWindowsViaCloud");

            if (transferResult != FileTransferResult.Successful)
            {
                Analytics.TrackEvent(sendEventText, "file", transferResult.ToString());

                if (transferResult != FileTransferResult.Cancelled)
                {
                    SetProgressText("Transfer failed.");
                    SetProgressValue(0, 1.0);
                    System.Diagnostics.Debug.WriteLine("Send failed.\r\n\r\n" + message);

                    if (transferResult == FileTransferResult.FailedOnHandshake)
                    {
                        message = "Couldn't reach remote device.<br />" +
                            "Make sure both devices are connected to the same Wi-Fi or LAN network.";
                    }

                    SetProgressSubtext(message);
                    ShowProgressButtons();
                }
            }
            else
            {
                SetProgressText("Done.");
                Analytics.TrackEvent(sendEventText, "file", "Success");
            }
        }

        private async Task<IEnumerable<FileSendInfo>> GetFiles(string[] files, bool preserveFolderStructure)
        {
            var fileSystem = new AndroidFileSystem(this);
            var items = files.Select(x => fileSystem.GetItemFromPathAsync(x).GetAwaiter().GetResult()).ToList();

            return preserveFolderStructure ? await GetFiles(items) : await GetFilesWithoutFolderStructure(items);
        }

        private async Task<List<FileSendInfo>> GetFilesWithoutFolderStructure(IEnumerable<IStorageItem> items)
        {
            var output = new List<FileSendInfo>();

            var files = items.OfType<IFile>().Select(x => new FileSendInfo(x)).ToList();
            var folders = items.OfType<AndroidFolder>().ToList();

            output.AddRange(files);

            foreach (var folder in folders)
            {
                output.AddRange(await GetFilesWithoutFolderStructure(await folder.GetItemsAsync()));
            }

            return output;
        }

        private async Task<List<FileSendInfo>> GetFiles(List<IStorageItem> items)
        {
            var output = new List<FileSendInfo>();

            var files = items.OfType<IFile>()
                .Select(x => new FileSendInfo(x, x.Path)).ToList();
            var folders = items.OfType<AndroidFolder>().ToList();

            var paths = files
                .Select(x => Path.GetDirectoryName(x.File.Path))
                .Union(folders.Select(x => x.Path))
                .Distinct();
            var rootFolderPath = new string(paths.Select(str => str.TakeWhile((c, index) => paths.All(s => s[index] == c))).FirstOrDefault().ToArray());
            if (folders.Count() == 1 && files.Count() == 0) // In case it's a single folder, exclude the folder name
                rootFolderPath = Path.GetDirectoryName(rootFolderPath) ?? rootFolderPath;

            output.AddRange(files);

            foreach (var folder in folders)
            {
                output.AddRange(await GetFilesOfFolder(folder, rootFolderPath));
            }

            return output;
        }

        private async Task<List<FileSendInfo>> GetFilesOfFolder(AndroidFolder f, string rootFolder = null)
        {
            if (rootFolder == null)
                rootFolder = f.Path;

            var items = await f.GetItemsAsync();

            List<FileSendInfo> files = (from x in items.OfType<IFile>()
                                        select new FileSendInfo(x, rootFolder)).ToList();

            var folders = items.OfType<AndroidFolder>().ToList();

            foreach (var folder in folders)
            {
                files.AddRange(await GetFilesOfFolder(folder, rootFolder));
            }

            return files;
        }

        private async Task SendFilesToRomeLocalDevice(string[] files, bool preserveFolderStructure)
        {
            Classes.Settings settings = new Classes.Settings(this);

            RomeAppServiceConnectionStatus result;
            SetProgressText("Connecting...");

            if (settings.UseInAppServiceOnWindowsDevices)
            {
                result = await Common.PackageManager.Connect(Common.GetCurrentRemoteSystem(), false, new Uri("roamit://receiveDialog"));
                //SetProgressText("Initializing...");
                //await Common.PackageManager.LaunchUri(new Uri("roamit://receiveDialog"), Common.GetCurrentRemoteSystem());
            }
            else
            {
                result = await Common.PackageManager.Connect(Common.GetCurrentRemoteSystem(), false);
            }

            if (result != RomeAppServiceConnectionStatus.Success)
            {
                Analytics.TrackEvent("SendToWindowsLocally", "file", "Failed");
                SetProgressText($"Connect failed. ({result.ToString()})");
                return;
            }

            var transferResult = await BeginSend(files, Common.PackageManager, preserveFolderStructure, sendFinishService: !settings.UseInAppServiceOnWindowsDevices);

            FinishSend(transferResult, isLocalRome: true);
        }

        private async Task<FileTransferResult> BeginSend(string[] filePaths, IRomePackageManager packageManager, bool preserveFolderStructure, bool sendFinishService)
        {
            SetProgressText((filePaths.Length == 1) ? "Retrieving file..." : "Retrieving files...");
            var files = (await GetFiles(filePaths, preserveFolderStructure)).ToList();

            SetProgressText("Preparing...");

            FileTransferResult transferResult = FileTransferResult.Successful;

            using (FileSender2 fs = new FileSender2(Common.GetCurrentNormalizedRemoteSystem(),
                                                    new WebServerComponent.WebServerGenerator(),
                                                    packageManager,
                                                    FindMyIPAddresses(),
                                                    (new Classes.Settings(this)).DeviceName))
            {
                fs.FileTransferProgress += (ss, ee) =>
                {
                    if (ee.State == FileTransferState.Error)
                    {
                        transferResult = FileTransferResult.FailedOnSend;
                    }
                    else if (ee.State == FileTransferState.Reconnecting)
                    {
                        RunOnUiThread(() =>
                        {
                            SetProgressText("Reconnecting...");
                            SetProgressValueToIndetermine();
                        });
                    }
                    else if (ee.State == FileTransferState.Reconnected)
                    {
                        RunOnUiThread(() =>
                        {
                            SetProgressText("Waiting for response...");
                        });
                    }
                    else
                    {
                        RunOnUiThread(() =>
                        {
                            SetProgressText((filePaths.Length == 1) ? "Sending file..." : "Sending files...");
                            SetProgressValue(ee.Progress, 1.0);
                        });
                    }
                };

                sendFileCancellationTokenSource = new CancellationTokenSource();
                if (filePaths.Length == 0)
                {
                    SetProgressText("No files.");
                    return FileTransferResult.NoFiles;
                }
                await Task.Run(async () =>
                {
                    transferResult = await fs.Send(files, sendFileCancellationTokenSource.Token);
                });
                sendFileCancellationTokenSource = null;
            }

            if (sendFinishService)
            {
                Dictionary<string, object> vs = new Dictionary<string, object>
                {
                    { "Receiver", "System" },
                    { "FinishService", "FinishService" },
                };
                await packageManager.Send(vs);
            }
            SetProgressValue(1.0, 1.0);

            return transferResult;
        }

        private IEnumerable<string> FindMyIPAddresses()
        {
            return NetworkHelper.GetLocalIPAddresses();
        }

        private async Task OpenUrl(string url)
        {
            ShowProgress();
            SetProgressText("Connecting...");

            RomeRemoteLaunchUriStatus result;
            if (IsCloudCommunication())
                result = await Common.RoamitCloudPackageManager.LaunchUri(new Uri(url), Common.GetCurrentNormalizedRemoteSystem());
            else
                result = await Common.PackageManager.LaunchUri(new Uri(url), Common.GetCurrentRemoteSystem());

            var eventTrackCat = IsCloudCommunication() ? (IsAndroidDevice() ? "SendToAndroid" : "SendToWindowsViaCloud") : "SendToWindowsLocally";

            SetProgressValue(1.0, 1.0);
            if (result == RomeRemoteLaunchUriStatus.Success)
            {
                SetProgressText("Done.");

                Analytics.TrackEvent(eventTrackCat, "launchUri", "Success");
            }
            else
            {
                SetProgressText(result.ToString());
                Analytics.TrackEvent(eventTrackCat, "launchUri", result.ToString());
            }
        }

        private async Task SendText(string text)
        {
            if (IsCloudCommunication())
                await SendTextToCloudDevice(text);
            else
                await SendTextToRomeLocalDevice(text);
        }

        private static bool IsCloudCommunication()
        {
            var type = Common.GetCurrentNormalizedRemoteSystem().Type;

            return (type == DeviceType.Android || type == DeviceType.GraphWindowsDevice);
        }

        private static bool IsAndroidDevice()
        {
            return (Common.GetCurrentNormalizedRemoteSystem().Type == DeviceType.Android);
        }

        private async Task SendTextToCloudDevice(string text)
        {
            ShowProgress();

            if (text.Length > 2048)
            {
                SetProgressText("Failed. (Text is too large)");
                SetProgressValue(0.0, 1.0);
                Analytics.TrackEvent((IsAndroidDevice() ? "SendToAndroid" : "SendToWindowsViaCloud"), "text", "TooLarge");

                return;
            }

            SetProgressText("Connecting...");

            if (!(await Common.RoamitCloudPackageManager.QuickClipboard(text, Common.GetCurrentNormalizedRemoteSystem(), (new Classes.Settings(this)).DeviceName)))
            {
                SetProgressText("Failed.");
                SetProgressValue(0.0, 1.0);
                Analytics.TrackEvent((IsAndroidDevice() ? "SendToAndroid" : "SendToWindowsViaCloud"), "text", "Failed");

                return;
            }

            SetProgressText("Done.");
            SetProgressValue(1.0, 1.0);
            Analytics.TrackEvent((IsAndroidDevice() ? "SendToAndroid" : "SendToWindowsViaCloud"), "text", "Success");
        }

        private async Task SendTextToRomeLocalDevice(string text)
        {
            ShowProgress();
            SetProgressText("Connecting...");

            if (!(await Common.PackageManager.QuickClipboard(text, Common.GetCurrentRemoteSystem(), (new Classes.Settings(this)).DeviceName, "roamit://clipboard")))
            {

                var result = await Common.PackageManager.Connect(Common.GetCurrentRemoteSystem(), false);

                if (result != QuickShare.Common.Rome.RomeAppServiceConnectionStatus.Success)
                {
                    SetProgressText($"Connect failed. ({result.ToString()})");
                    Analytics.TrackEvent("SendToWindows", "text", "Failed");
                    return;
                }

                //Fix Rome Android bug (receiver app service closes after 5 seconds in first connection)
                Common.PackageManager.CloseAppService();
                result = await Common.PackageManager.Connect(Common.GetCurrentRemoteSystem(), false);

                if (result != QuickShare.Common.Rome.RomeAppServiceConnectionStatus.Success)
                {
                    SetProgressText($"Connect failed. ({result.ToString()})");
                    Analytics.TrackEvent("SendToWindows", "text", result.ToString());
                    return;
                }

                TextSender textSender = new TextSender(Common.PackageManager, (new Classes.Settings(this)).DeviceName);

                textSender.TextSendProgress += (ee) =>
                {
                    SetProgressValue((double)ee.SentParts, (double)ee.TotalParts);
                };

                SetProgressText("Sending...");

                bool sendResult = await textSender.Send(text, ContentType.ClipboardContent);

                SetProgressValue(1.0, 1.0);

                if (!sendResult)
                {
                    SetProgressText("Send failed.");
                    SetProgressValue(1.0, 1.0);
                    Analytics.TrackEvent("SendToWindows", "text", "Failed");
                    return;
                }

                Common.PackageManager.CloseAppService();
            }

            SetProgressText("Done.");
            SetProgressValue(1.0, 1.0);
            Analytics.TrackEvent("SendToWindows", "text", "Success");
        }
        #endregion

        class HybridWebViewClient : WebViewClient
        {
            WebViewContainerActivity context;
            public HybridWebViewClient(WebViewContainerActivity _context)
            {
                context = _context;
            }

            public override void OnPageFinished(WebView view, string url)
            {
                if (url.Contains("#progress"))
                    return;

                if ((context.Intent.Action == Intent.ActionSend) || (context.Intent.Action == Intent.ActionSendMultiple))
                {
                    string previewText = "Unsupported content.";

                    if (Common.ShareFiles != null)
                    {
                        if (Common.ShareFiles.Length == 1)
                            previewText = Common.ShareFiles[0];
                        else
                            previewText = $"{Common.ShareFiles.Length} files";
                    }
                    else if (Common.ShareText.Length > 0)
                    {
                        previewText = Common.ShareText;
                    }

                    context.SendJavascriptToWebView($"setSharePreview('{previewText.NormalizeForJsCall()}');");
                }

                if ((Common.ListManager != null) && (Common.ListManager.RemoteSystems != null) && (Common.ListManager.RemoteSystems.Count > 0))
                {
                    foreach (var item in Common.ListManager.RemoteSystems)
                        context.AddRemoteSystemToList(item);

                    if (Common.ListManager.SelectedRemoteSystem == null)
                    {
                        context.SelectItemIfNecessary(true);
                    }
                    else
                    {
                        context.AddRemoteSystemToList(Common.ListManager.SelectedRemoteSystem);
                        context.automaticRemoteSystemSelectionAllowed = false;

                        var s = $"selectItem('{Common.ListManager.SelectedRemoteSystem?.Id?.NormalizeForJsCall() ?? ""}');";
                        context.SendJavascriptToWebView(s);
                    }
                }

                base.OnPageFinished(view, url);
            }

            [Obsolete]
            public override bool ShouldOverrideUrlLoading(WebView webView, string url)
            {
                System.Diagnostics.Debug.WriteLine($"** {url}");
                if (url == "file:///android_asset/html/settings.html")
                {
                    var intent = new Intent(context, typeof(SettingsActivity));
                    context.StartActivityForResult(intent, context.SettingsId);
                }
                else if (url == "file:///android_asset/html/stopAutomaticSelection.html")
                {
                    context.automaticRemoteSystemSelectionAllowed = false;
                }
                else if (url == "file:///android_asset/html/sendFile.html")
                {
                    context.automaticRemoteSystemSelectionAllowed = false;
                    ProcessRequest("File");
                }
                else if (url == "file:///android_asset/html/sendPhoto.html")
                {
                    context.automaticRemoteSystemSelectionAllowed = false;
                    ProcessRequest("Photo");
                }
                else if (url == "file:///android_asset/html/sendClipboard.html")
                {
                    context.automaticRemoteSystemSelectionAllowed = false;
                    ProcessRequest("Clipboard");
                }
                else if (url == "file:///android_asset/html/sendUrl.html")
                {
                    context.automaticRemoteSystemSelectionAllowed = false;
                    ProcessRequest("Url");
                }
                else if (url.Contains("file:///android_asset/html/selectItem.html"))
                {
                    context.automaticRemoteSystemSelectionAllowed = false;
                    var id = url.Split('?')[1];
                    context.SelectDevice(id);
                }
                else if (url == "file:///android_asset/html/shareFile.html")
                {
                    context.automaticRemoteSystemSelectionAllowed = false;
                    ProcessRequest("Share_File");
                }
                else if (url == "file:///android_asset/html/shareClipboard.html")
                {
                    context.automaticRemoteSystemSelectionAllowed = false;
                    ProcessRequest("Share_Text");
                }
                else if (url == "file:///android_asset/html/shareLink.html")
                {
                    context.automaticRemoteSystemSelectionAllowed = false;
                    ProcessRequest("Share_Url");
                }
                else if (url == "file:///android_asset/html/rate.html")
                {
                    LaunchUrl("market://details?id=" + Application.Context.PackageName);
                }
                else if (url == "file:///android_asset/html/getExtensionLink.html")
                {
                    LaunchUrl(Constants.BrowserExtensionsUrl);
                }
                else if (url == "file:///android_asset/html/getAndroidAppLink.html")
                {
                    LaunchUrl(Constants.GooglePlayAppUrl);
                }
                else if (url == "file:///android_asset/html/getWindows10AppLink.html")
                {
                    LaunchUrl(Constants.WindowsStoreAppUrl);
                }
                else if (url == "file:///android_asset/html/contact.html")
                {
                    var mailto = "mailto:roamitapp@gmail.com?subject=Roamit%20for%20Android%20v" + Application.Context.ApplicationContext.PackageManager.GetPackageInfo(Application.Context.ApplicationContext.PackageName, 0).VersionName;

                    var email = new Intent(Intent.ActionSendto);
                    email.SetData(Android.Net.Uri.Parse(mailto));

                    if (context.PackageManager.QueryIntentActivities(email, 0).Count > 0)
                    {
                        context.StartActivity(email);
                    }
                }
                else if (url == "file:///android_asset/html/loginWarningAuthorize.html")
                {
                    
                }
                else if (url == "file:///android_asset/html/sendFileActionRetry.html")
                {
                    ProcessRequest("SendFile_Retry");
                }
                else if (url == "file:///android_asset/html/sendFileActionCancel.html")
                {
                    ProcessRequest("SendFile_Cancel");
                }
                else if (url == "file:///android_asset/html/history.html")
                {
                    var intent = new Intent(context, typeof(HistoryListActivity));
                    context.StartActivity(intent);
                }
                //else if (url == "file:///android_asset/html/createDummyHistory.html")
                //{
                //    var r = new Random();
                //    DateTime dt = DateTime.Now;

                //    for (int i = 0; i < 500; i++)
                //    {
                //        DataStore.DataStorageProviders.HistoryManager.OpenAsync().Wait();
                //        DataStore.DataStorageProviders.TextReceiveContentManager.OpenAsync().Wait();
                //        dt = dt.AddSeconds(1);
                //        var guid = Guid.NewGuid();
                //        DataStore.IReceivedData data;
                //        switch (r.Next(3))
                //        {
                //            case 0:
                //                var x1 = new DataStore.ReceivedFileCollection
                //                {
                //                    StoreRootPath = "/"
                //                };

                //                for (int j = 0; j < r.Next(1, 10); j++)
                //                {
                //                    x1.Files.Add(new DataStore.ReceivedFile()
                //                    {
                //                        Completed = true,
                //                        Name = $"File{j}",
                //                        Size = j + 1,
                //                        StorePath = "/",
                //                    });
                //                }

                //                data = x1;
                //                break;
                //            case 1:
                //                data = new DataStore.ReceivedText();

                //                DataStore.DataStorageProviders.TextReceiveContentManager.Add(guid, $"Clipboard content #{i}!");

                //                break;
                //            case 2:
                //                data = new DataStore.ReceivedUrl
                //                {
                //                    Uri = new Uri($"http://www.google.com/?x={i}"),
                //                };
                //                break;
                //            default:
                //                throw new Exception("wat");
                //        }

                //        DataStore.DataStorageProviders.HistoryManager.Add(guid, dt, $"Dummy #{i}", data, true);
                //        DataStore.DataStorageProviders.TextReceiveContentManager.Close();
                //        DataStore.DataStorageProviders.HistoryManager.Close();
                //    }

                //}
                else
                {
                    return false;
                }

                return true;
            }

            private void LaunchUrl(string url)
            {
                var uri = Android.Net.Uri.Parse(url);
                Intent intent = new Intent(Intent.ActionView, uri);

                if (context.PackageManager.QueryIntentActivities(intent, 0).Count > 0)
                {
                    context.StartActivity(intent);
                }
            }

            private async void ProcessRequest(string contentType)
            {
                bool isFromShareTarget = false;

                switch (contentType)
                {
                    case "Clipboard":
                        await context.SendText(ClipboardHelper.GetClipboardText(context));
                        break;
                    case "Photo":
                        await context.PickAndSendPicture();
                        break;
                    case "File":
                        await context.PickAndSendFile();
                        break;
                    case "Url":
                        await context.OpenUrl(ClipboardHelper.GetClipboardText(context));
                        break;
                    case "Share_File":
                        isFromShareTarget = true;
                        await context.SendFiles(Common.ShareFiles, true);
                        break;
                    case "Share_Url":
                        isFromShareTarget = true;
                        await context.OpenUrl(Common.ShareText);
                        break;
                    case "Share_Text":
                        isFromShareTarget = true;
                        await context.SendText(Common.ShareText);
                        break;
                    case "SendFile_Retry":
                        await context.RetrySendFiles();
                        break;
                    case "SendFile_Cancel":
                        context.OnBackPressed();
                        break;
                    case "Unknown":
                    default:
                        Analytics.TrackException($"SendPageActivity: Unknown contentType '{contentType}'.", false);
                        break;
                }

                if (isFromShareTarget)
                    Analytics.TrackEvent("Send", "ShareTarget");
                else
                    Analytics.TrackEvent("Send", "WithinApp");
            }

        }
    }
}