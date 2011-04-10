namespace Demo
{
    using System;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media.Imaging;

    public partial class MainWindow : Window
    {
        public static readonly DependencyProperty AvailableImageCountProperty = DependencyProperty.Register("AvailableImageCount", 
                                                                                                            typeof(int), 
                                                                                                            typeof(MainWindow));

        public static readonly DependencyProperty ControlCountProperty = DependencyProperty.Register("ControlCount", 
                                                                                                     typeof(int), typeof(MainWindow),
                                                                                                     new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsRender, HandleControlCountChange));
        private ObservableCollection<UIElement> controls;

        public MainWindow()
        {
            this.controls = new ObservableCollection<UIElement>();
            this.DataContext = this.controls;

            PopulateControls();

            InitializeComponent();
        }

        Image[] images;
        private void PopulateControls()
        {
            if (this.images == null)
            {
                this.images = CreateImageArray();

                AvailableImageCount = this.images.Length;
            }

            for (int i = 0; i < AvailableImageCount && i < ControlCount; i++)
            {
                this.controls.Add(this.images[i]);
            }
        }

        private Image[] CreateImageArray()
        {
            string curAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;
            string curPath = Path.GetDirectoryName(curAssembly);

            string[] pics = Directory.GetFiles(curPath, "*.jpg");

            Uri[] picUris = Array.ConvertAll<string, Uri>(pics, (fp) => new Uri(fp));
            Image[] createdImages = new Image[picUris.Length];

            for (int i = 0; i < picUris.Length; i++)
            {
                Image image = new Image();
                image.Name = Path.GetFileNameWithoutExtension(picUris[i].LocalPath);

                // try loading the image from the document's Uri
                BitmapImage bitmapImage = new BitmapImage(picUris[i]);
                image.Source = bitmapImage;
                image.HorizontalAlignment = HorizontalAlignment.Stretch;
                image.VerticalAlignment = VerticalAlignment.Stretch;
                image.Stretch = System.Windows.Media.Stretch.Fill;

                createdImages[i] = image;
            }

            return createdImages;
        }

        private static void HandleControlCountChange(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            MainWindow mw = sender as MainWindow;
            if (mw != null)
            {
                mw.HandleControlCountChange(args);
            }
        }

        private void HandleControlCountChange(DependencyPropertyChangedEventArgs args)
        {
            int oldCount = (int)args.OldValue;
            int newCount = (int)args.NewValue;

            //removed some controls
            if (oldCount > newCount)
            {
                while (this.controls.Count > newCount)
                {
                    this.controls.RemoveAt(this.controls.Count - 1);
                }
            }
            else if (oldCount < newCount)
            {
                for (int i = oldCount; i < newCount; i++)
                {
                    this.controls.Add(this.images[i]);
                }
            }
        }

        private void OnChecked(object sender, RoutedEventArgs args)
        {
            RadioButton rb = sender as RadioButton;

            if (rb != null)
            {
                //the BAML reader will set the intitial state of one of the radio buttons, ending up calling
                //this method before the NGonDisplayControl has actually been created, we can ignore that since
                //our default radio button is the Horizontal one and that is the NGonDisplayControl's default
                //layout orientation.
                if (NGonDisplayControl != null)
                {
                    if (NGonDisplayControl.Orientation != (Orientation)rb.Tag)
                    {
                        NGonDisplayControl.Orientation = (Orientation)rb.Tag;
                    }
                }
            }
        }

        public int AvailableImageCount
        {
            get
            {
                return (int)GetValue(MainWindow.AvailableImageCountProperty);
            }
            set
            {
                SetValue(MainWindow.AvailableImageCountProperty, value);
            }
        }

        public int ControlCount
        {
            get
            {
                return (int)GetValue(MainWindow.ControlCountProperty);
            }
            set
            {
                SetValue(MainWindow.ControlCountProperty, value);
            }
        }

        private void HandlePreviousClick(object sender, RoutedEventArgs args)
        {
            NGonDisplayControl.MovePrevious();
        }

        private void HandleNextClick(object sender, RoutedEventArgs args)
        {
            NGonDisplayControl.MoveNext();
        }        
    }
}