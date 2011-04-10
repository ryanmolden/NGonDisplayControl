namespace Demo
{
    using System;
    using System.Windows.Data;

    /// <summary>
    /// Converts from a TimeSpane input value -> string form of the TimeSpan in milliseconds, and vice-versa.
    /// </summary>
    internal sealed class MillisecondToTimeSpanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            TimeSpan timeSpan = (TimeSpan)value;
            if (timeSpan != null)
            {
                return ((int)timeSpan.TotalMilliseconds).ToString();
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string input = (string)value;
            if (input != null)
            {
                int parsedInt;
                if (Int32.TryParse(input, out parsedInt))
                {
                    //no setting times over a minute for animations ;)
                    if (parsedInt <= 60000)
                    {
                        int fullSeconds = parsedInt / 1000;
                        double milliseconds = ((double)(parsedInt % 1000)) / 1000;

                        TimeSpan result;
                        string parseString = String.Format("0:0:{0}{1:f.0}", (fullSeconds != 0) ? fullSeconds.ToString() + "." : String.Empty, milliseconds.ToString());
                        if (TimeSpan.TryParse(parseString, out result))
                        {
                            return result;
                        }
                    }
                }
            }

            return Binding.DoNothing;
        }
    }
}