﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Notifications;

namespace QuickShare.HelperClasses
{
    public static class ToastFunctions
    {
        public static void SendToast(string title, string text)
        {
            const ToastTemplateType toastTemplate = ToastTemplateType.ToastText02;
            var toastXml = ToastNotificationManager.GetTemplateContent(toastTemplate);

            var toastTextElements = toastXml.GetElementsByTagName("text");
            toastTextElements[0].AppendChild(toastXml.CreateTextNode(title));
            toastTextElements[1].AppendChild(toastXml.CreateTextNode(text));

            var toast = new ToastNotification(toastXml);
            ToastNotificationManager.CreateToastNotifier().Show(toast);
        }

        public static void SendUniversalClipboardNoticeToast()
        {
            SendToast("Roamit universal clipboard will run in the background",
                "You can see your clipboard history by clicking the Roamit icon in the system tray.");
        }
    }
}
