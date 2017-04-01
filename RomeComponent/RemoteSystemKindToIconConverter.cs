﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml.Data;

namespace QuickShare.Rome
{
    public class RemoteSystemKindToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if ((value is string))
            {
                var system = value as string;
                System.Diagnostics.Debug.WriteLine(system);
                switch (system)
                {
                    case "Xbox":
                        return Char.ConvertFromUtf32(0xE7FC);
                    case "Mobile":
                    case "Phone":
                        return Char.ConvertFromUtf32(0xE8EA);
                    default:
                        return Char.ConvertFromUtf32(0xE7F8);
                }
            }

            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}