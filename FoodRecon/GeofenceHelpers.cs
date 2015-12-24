//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Devices.Geolocation;
using Windows.Devices.Geolocation.Geofencing;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace FoodRecon
{
    public sealed partial class MainPage : Page
    {
        private string GetTimeStampedMessage(string eventCalled)
        {
            calendar.SetToNow();
            return eventCalled + " " + formatterLongTime.Format(calendar.GetDateTime());
        }

        private enum TimeComponent
        {
            day,
            hour,
            minute,
            second
        }

        private long ParseTimeSpan(string field, int defaultValue)
        {
            long timeSpanValue = 0;
            char[] delimiterChars = { ':' };
            string[] timeComponents = field.Split(delimiterChars);
            int start = 4 - timeComponents.Length;

            if (start >= 0)
            {
                int loop = 0;
                int index = start;
                for (; loop < timeComponents.Length; loop++, index++)
                {
                    TimeComponent tc = (TimeComponent)index;

                    switch (tc)
                    {
                        case TimeComponent.day:
                            timeSpanValue += (long)decimalFormatter.ParseInt(timeComponents[loop]) * secondsPerDay;
                            break;

                        case TimeComponent.hour:
                            timeSpanValue += (long)decimalFormatter.ParseInt(timeComponents[loop]) * secondsPerHour;
                            break;

                        case TimeComponent.minute:
                            timeSpanValue += (long)decimalFormatter.ParseInt(timeComponents[loop]) * secondsPerMinute;
                            break;

                        case TimeComponent.second:
                            timeSpanValue += (long)decimalFormatter.ParseInt(timeComponents[loop]);
                            break;

                        default:
                            break;
                    }
                }
            }

            if (0 == timeSpanValue)
            {
                timeSpanValue = defaultValue;
            }

            timeSpanValue *= oneHundredNanosecondsPerSecond;

            return timeSpanValue;
        }
    }
}
