﻿using QuickShare.Classes;
using QuickShare.Common;
using QuickShare.DevicesListManager;
using QuickShare.UWP.Rome;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Windows.UI.Xaml;

namespace QuickShare.ViewModels
{
    public class MainPageViewModel : INotifyPropertyChanged, IDisposable
    {
        public DevicesListManager.DevicesListManager ListManager { get; } = new DevicesListManager.DevicesListManager("", new RemoteSystemNormalizer());

        public MainPageViewModel()
        {
            ListManager.PropertyChanged += ListManager_PropertyChanged;
            DevicesList = ListManager.RemoteSystems;
        }

        private void ListManager_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if ((e.PropertyName == "SelectedRemoteSystem") && (ListManager.SelectedRemoteSystem != null))
                if ((selectedRemoteSystem == null) || (selectedRemoteSystem.Id != ListManager.SelectedRemoteSystem.Id))
                    SelectedRemoteSystem = ListManager.SelectedRemoteSystem;
        }

        ObservableCollection<NormalizedRemoteSystem> devicesList;
        public ObservableCollection<NormalizedRemoteSystem> DevicesList
        {
            get { return devicesList; }
            set
            {
                devicesList = value;
                OnPropertyChanged("DevicesList");
            }
        }

        private NormalizedRemoteSystem selectedRemoteSystem;
        public NormalizedRemoteSystem SelectedRemoteSystem
        {
            get { return selectedRemoteSystem; }
            set
            {
                selectedRemoteSystem = value;
                OnPropertyChanged("SelectedRemoteSystem");
            }
        }

        private string caption = "";
        public string Caption
        {
            get { return caption; }
            set
            {
                if (value.Length == 0)
                    caption = "";
                else
                    caption = value + " - ‌"; //NOTE: This includes a NimFasele in the end of string.
                OnPropertyChanged("Caption");
            }
        }

        private Visibility backButtonPlaceholderVisibility;
        public Visibility BackButtonPlaceholderVisibility
        {
            get { return backButtonPlaceholderVisibility; }
            set
            {
                backButtonPlaceholderVisibility = value;
                OnPropertyChanged("BackButtonPlaceholderVisibility");
            }
        }

        private Visibility upgradeButtonVisibility = Visibility.Collapsed;
        public Visibility UpgradeButtonVisibility
        {
            get { return upgradeButtonVisibility; }
            set
            {
                upgradeButtonVisibility = value;
                OnPropertyChanged("UpgradeButtonVisibility");
            }
        }

        public Visibility LookingForDevicesVisibility
        {
            get
            {
                if ((ListManager.RemoteSystems == null) || ((ListManager.RemoteSystems.Count == 0) && (ListManager.SelectedRemoteSystem == null)))
                    return Visibility.Visible;
                return Visibility.Collapsed;
            }
        }

        private Visibility signInNoticeVisibility = Visibility.Collapsed;
        public Visibility SignInNoticeVisibility
        {
            get { return signInNoticeVisibility; }
            set
            {
                signInNoticeVisibility = value;
                OnPropertyChanged("SignInNoticeVisibility");
                OnPropertyChanged("OverlayVisibility");
            }
        }

        private Visibility whatsNewVisibility = Visibility.Collapsed;
        public Visibility WhatsNewVisibility
        {
            get { return whatsNewVisibility; }
            set
            {
                whatsNewVisibility = value;
                OnPropertyChanged("WhatsNewVisibility");
                OnPropertyChanged("OverlayVisibility");
            }
        }

        private Visibility donateFlyoutVisibility = Visibility.Collapsed;
        public Visibility DonateFlyoutVisibility
        {
            get { return donateFlyoutVisibility; }
            set
            {
                donateFlyoutVisibility = value;
                OnPropertyChanged("DonateFlyoutVisibility");
                OnPropertyChanged("OverlayVisibility");
            }
        }

        private Visibility signInToCloudServiceFlyoutVisibility = Visibility.Collapsed;
        public Visibility SignInToCloudServiceFlyoutVisibility
        {
            get { return signInToCloudServiceFlyoutVisibility; }
            set
            {
                signInToCloudServiceFlyoutVisibility = value;
                OnPropertyChanged("SignInToCloudServiceFlyoutVisibility");
                OnPropertyChanged("OverlayVisibility");
            }
        }

        public Visibility OverlayVisibility
        {
            get
            {
                if ((signInNoticeVisibility == Visibility.Visible) ||
                    (whatsNewVisibility == Visibility.Visible) || 
                    (donateFlyoutVisibility == Visibility.Visible) ||
                    (signInToCloudServiceFlyoutVisibility == Visibility.Visible))
                    return Visibility.Visible;

                return Visibility.Collapsed;
            }
        }

        public Visibility SignInWarningVisibility
        {
            get => (SecureKeyStorage.IsTokenStored() && SecureKeyStorage.IsAccountIdStored()) ? Visibility.Collapsed : Visibility.Visible;
        }

        private bool contentFrameNeedsRemoteSystemSelection = false;
        public bool ContentFrameNeedsRemoteSystemSelection
        {
            get
            {
                return contentFrameNeedsRemoteSystemSelection;
            }
            set
            {
                contentFrameNeedsRemoteSystemSelection = value;
                OnPropertyChanged("IsContentFrameEnabled");
            }
        }

        private bool frameBottomPaddingEnabled = false;
        public bool FrameBottomPaddingEnabled
        {
            get
            {
                return frameBottomPaddingEnabled;
            }
            set
            {
                frameBottomPaddingEnabled = value;
                OnPropertyChanged("FrameBottomPaddingEnabled");
            }
        }

        private bool isDevicesListExpanded = false;
        public bool IsDevicesListExpanded
        {
            get
            {
                return isDevicesListExpanded;
            }
            set
            {
                isDevicesListExpanded = value;
                OnPropertyChanged("IsDevicesListExpanded");
            }
        }

        public bool IsContentFrameEnabled
        {
            get
            {
                if (ContentFrameNeedsRemoteSystemSelection)
                    return ListManager.SelectedRemoteSystem != null;
                return true;
            }
        }

        public void RemoteSystemCollectionChanged()
        {
            try
            {
                OnPropertyChanged("IsContentFrameEnabled");
                OnPropertyChanged("LookingForDevicesVisibility");

                if (IsContentFrameEnabled)
                {
                    var content = MainPage.Current.InternalFrameContent as IKindChangeAware;
                    content?.SelectedRemoteSystemChanged(ListManager.SelectedRemoteSystem.Kind);
                }
            }
            catch { }
        }

        private bool isAcrylicEnabled = false;
        public bool IsAcrylicEnabled
        {
            get { return isAcrylicEnabled; }
            set
            {
                isAcrylicEnabled = value;
                OnPropertyChanged("IsAcrylicEnabled");
                OnPropertyChanged("CustomTopBarVisibility");
                OnPropertyChanged("FramePadding");
            }
        }

        public Visibility CustomTopBarVisibility
        {
            get
            {
                return IsAcrylicEnabled ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        // Create the OnPropertyChanged method to raise the event
        protected void OnPropertyChanged(string name)
        {
            try
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
            catch { }
        }

        public void Dispose()
        {
            ListManager.PropertyChanged -= ListManager_PropertyChanged;
        }

        public void UpdateSignInWarningVisibility()
        {
            OnPropertyChanged("SignInWarningVisibility");
        }
    }
}