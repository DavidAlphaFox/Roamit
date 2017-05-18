﻿using Android.App;
using Android.Widget;
using Android.OS;
using System;
using System.Collections.ObjectModel;
using QuickShare.Droid.RomeComponent;
using System.Collections.Generic;
using QuickShare.DevicesListManager;
using Microsoft.ConnectedDevices;
using Android.Webkit;
using System.Linq;
using Android.Content;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace QuickShare.Droid
{
    [Activity(Label = "QuickShare.Droid", MainLauncher = true, Icon = "@drawable/icon")]
    public class MainActivity : Activity
    {
        DevicesListAdapter devicesAdapter;
        ListView listView;

        private WebView _webView;
        internal Dialog _authDialog;

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);

            Common.PackageManager = new RomePackageManager(this);
            Common.PackageManager.Initialize("com.quickshare.service");
            Common.PackageManager.RemoteSystems.CollectionChanged += RemoteSystems_CollectionChanged;

            devicesAdapter = new DevicesListAdapter(this, Common.ListManager);
            listView = FindViewById<ListView>(Resource.Id.listView1);
            listView.Adapter = devicesAdapter;
            listView.ItemClick += ListView_ItemClick;
            listView.ItemSelected += ListView_ItemSelected;

            InitDiscovery();

            FindViewById<Button>(Resource.Id.button3).Click += Button3_Click;
            FindViewById<Button>(Resource.Id.mainSendClipboard).Click += SendClipboard_Click;
            FindViewById<Button>(Resource.Id.mainSendFile).Click += SendFile_Click;

            //InitWebServer();
        }

        private void SendFile_Click(object sender, EventArgs e)
        {
            var intent = new Intent(this, typeof(SendPageActivity));
            intent.PutExtra("ContentType", "File");
            StartActivity(intent);
        }

        //WebServerComponent.WebServer web = new WebServerComponent.WebServer();
        //private void InitWebServer()
        //{
        //    var ip = NetworkHelper.GetLocalIPAddress();
        //    Toast.MakeText(this, ip, ToastLength.Long).Show();

        //    web.StartWebServer(ip, 8081);
        //}

        private void SendClipboard_Click(object sender, EventArgs e)
        {
            var intent = new Intent(this, typeof(SendPageActivity));
            intent.PutExtra("ContentType", "Clipboard");
            StartActivity(intent);

            /**
            System.Diagnostics.Debug.WriteLine("Connect()");
            var result = await packageManager.Connect(GetCurrentRemoteSystem(), false);
            System.Diagnostics.Debug.WriteLine($"Connect() finished with result {result.ToString()}");
            //Toast.MakeText(this, result.ToString(), ToastLength.Short).Show();

            Dictionary<string, object> data = new Dictionary<string, object>
            {
                { "Receiver", "System" },
                { "Platform", "Android" }
            };
            var sendResult = await packageManager.Send(data);
            System.Diagnostics.Debug.WriteLine($"Send finished with result {sendResult.ToString()}");
            Toast.MakeText(this, sendResult.ToString(), ToastLength.Long);
            /**/
        }

        private void ListView_ItemClick(object sender, AdapterView.ItemClickEventArgs e)
        {
            Common.ListManager.Select(Common.ListManager.RemoteSystems[e.Position]);

            FindViewById<TextView>(Resource.Id.selectedDeviceName).Text = Common.ListManager.SelectedRemoteSystem.DisplayName;
            System.Diagnostics.Debug.WriteLine(Common.ListManager.SelectedRemoteSystem.DisplayName + " is selected.");
        }

        private void ListView_ItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            e.View.Selected = true;
        }

        private async void Button3_Click(object sender, EventArgs e)
        {
            RemoteSystem rs = Common.GetCurrentRemoteSystem();

            if (rs == null)
            {
                Toast.MakeText(this, "Device not found.", ToastLength.Long).Show();
                return;
            }

            var result = await Common.PackageManager.LaunchUri(new Uri("http://www.ghiasi.net"), rs);
            Toast.MakeText(this, result.ToString(), ToastLength.Long).Show();

            var c = await Common.PackageManager.Connect(rs, false);
            //Fix Rome Android bug (receiver app service closes after 5 seconds in first connection)
            Common.PackageManager.CloseAppService();
            c = await Common.PackageManager.Connect(rs, false);

            //Common.PeriodicalPing();
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                {"TestLongRunning", "TestLongRunning" },
            };
            await Common.PackageManager.Send(data);
        }

        private async void InitDiscovery()
        {
            Platform.FetchAuthCode += Platform_FetchAuthCode;
            await Common.PackageManager.InitializeDiscovery();
        }

        private void RemoteSystems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (var item in e.NewItems)
                {
                    Common.ListManager.AddDevice(item);
                }

            if (e.OldItems != null)
                foreach (var item in e.OldItems)
                {
                    Common.ListManager.RemoveDevice(item);
                }
        }

        private void Platform_FetchAuthCode(string oauthUrl)
        {
            _authDialog = new Dialog(this);

            var linearLayout = new LinearLayout(_authDialog.Context);
            _webView = new WebView(_authDialog.Context);
            linearLayout.AddView(_webView);
            _authDialog.SetContentView(linearLayout);

            _webView.SetWebChromeClient(new WebChromeClient());
            _webView.Settings.JavaScriptEnabled = true;
            _webView.Settings.DomStorageEnabled = true;
            _webView.LoadUrl(oauthUrl);

            _webView.SetWebViewClient(new MsaWebViewClient(this));
            _authDialog.Show();
            _authDialog.SetCancelable(true);
        }
    }
}
