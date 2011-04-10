namespace Demo
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Markup;
    using System.Windows.Media;
    using System.Windows.Media.Animation;
    using System.Windows.Media.Media3D;

    //BUGBUG:  Doesn't handle a large number of UIElements very efficeintly ;)  Tried placing 500 buttons in it, real slow to do
    //layout caclculations and the rotations make it hard to tell it is 3d since the exterior angle between faces is very 
    //close to 180 degrees
    //
    //BUGBUG:  Doesn't allow users to style the items in the controls in any special manner, could imagine displaying some border
    //around the item or header above/below it.
    //
    //BUGBUG:  Doesn't handle variable sized content at all, since things are always in a Viewport3D (right now) they will
    //always fill the Viewport area, so smaller items will be streched and larger items will be shrunk.
    [ContentPropertyAttribute("Items")]
    public partial class NGonDisplayControl : ContentControl, INotifyPropertyChanged
    {
        #region Private Fields

        private bool viewportNeedsReset;

        private static readonly Vector3D xAxisVector = new Vector3D(1.0, 0.0, 0.0);
        private static readonly Vector3D yAxisVector = new Vector3D(0.0, 1.0, 0.0);

        private Point3D[] vertexPoints;
        private bool[] calculatedFaces;

        private bool canRotateForward;
        private bool canRotateBack;

        private bool lightsAdded;

        //stores the information to ensure our camera looks right when positioned looking at an arbitrary face, recalculated
        //when the numbers of faces changes
        private Point3D cameraPosition;
        private Vector3D cameraUpDirection;
        private Vector3D faceNormal;

        private QuaternionRotation3D cameraAngleRotation;
        private ScaleTransform3D cameraScaleTransform;
        private Transform3DGroup cameraTransformGroup;

        //Debugging aid, allows a rendering of all the items in our control since we can only see one at a time in the
        //actual control
        private ObservableCollection<VisualBrush> brushes = new ObservableCollection<VisualBrush>();
        private ObservableCollection<string> debuggingInfo = new ObservableCollection<string>();

        private Viewport3D viewport3D;
        private PerspectiveCamera viewportCamera;

        private int currentFaceIndex = 0;

        #endregion

        #region Dependency Properties

        /// <summary>
        /// Property that controls the amount of margin applied to each face of the N-Gon, this allows for gaps between each side to allow the user to see through to items along
        /// the edge farthest from the camera.
        /// </summary>
        public static readonly DependencyProperty FaceMarginProperty = DependencyProperty.Register("FaceMargin", 
                                                                                                   typeof(double), 
                                                                                                   typeof(NGonDisplayControl), 
                                                                                                   new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.None, HandleFaceMarginChanged));

        /// <summary>
        /// Property for the item currently being displayed in the 2d content controls Content area.
        /// </summary>
        public static readonly DependencyProperty CurrentItemProperty = DependencyProperty.Register("CurrentItem", 
                                                                                                    typeof(UIElement), 
                                                                                                    typeof(NGonDisplayControl), 
                                                                                                    new FrameworkPropertyMetadata(HandleCurrentItemPropertyChanged));

        /// <summary>
        /// Property that controls how long the zoom in/out effect takes when doing the face transition animations.
        /// </summary>
        public static readonly DependencyProperty ZoomAnimationTimeProperty = DependencyProperty.Register("ZoomAnimationTime", 
                                                                                                          typeof(TimeSpan),
                                                                                                          typeof(NGonDisplayControl), 
                                                                                                          new FrameworkPropertyMetadata(TimeSpan.FromMilliseconds(250)));

        /// <summary>
        /// Property that controls how long the rotation between current and target face on the N-Gon takes.
        /// </summary>
        public static readonly DependencyProperty RotationAnimationTimeProperty = DependencyProperty.Register("RotationAnimationTime", 
                                                                                                              typeof(TimeSpan), 
                                                                                                              typeof(NGonDisplayControl), 
                                                                                                              new FrameworkPropertyMetadata(TimeSpan.FromMilliseconds(350)));

        /// <summary>
        /// Property that contains the lights for the 3d viewport to affect the display of items during the 3d transitions.
        /// </summary>
        public static readonly DependencyProperty ViewPortLightsProperty = DependencyProperty.Register("ViewPortLights", 
                                                                                                       typeof(IEnumerable<Light>), 
                                                                                                       typeof(NGonDisplayControl), 
                                                                                                       new FrameworkPropertyMetadata(CreateDefaultLights(), FrameworkPropertyMetadataOptions.None, HandleLightCollectionChanged));

        /// <summary>
        /// Property which holds the items to display on each of face of the N-Gon.
        /// </summary>
        public static readonly DependencyProperty ItemsProperty = DependencyProperty.RegisterAttached("Items", 
                                                                                                      typeof(ICollection<UIElement>), 
                                                                                                      typeof(NGonDisplayControl),
                                                                                                      new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.None, HandleItemsPropertyChanged));

        /// <summary>
        /// Property that controls whether the N-Gon is vertically or horizontally aligned (affects whether it rotates left <-> right (horizontally aligned) or top <-> Bottom (vertically aligned).
        /// </summary>
        public static readonly DependencyProperty OrientationProperty = StackPanel.OrientationProperty.AddOwner(typeof(NGonDisplayControl), 
                                                                                                                new FrameworkPropertyMetadata(Orientation.Horizontal, HandleOrientationChanged));

        #endregion

        #region Constructor

        public NGonDisplayControl()
        {
            this.cameraTransformGroup = new Transform3DGroup();

            //used for zoom effects
            this.cameraScaleTransform = new ScaleTransform3D(1.0, 1.0, 1.0);
            this.cameraTransformGroup.Children.Add(this.cameraScaleTransform);

            //used for rotation effects
            this.cameraAngleRotation = new QuaternionRotation3D();
            this.cameraTransformGroup.Children.Add(new RotateTransform3D(this.cameraAngleRotation));

            InitializeComponent();

            PerspectiveCamera camera = new PerspectiveCamera();
            //Initially positioned at +3 the Z-axis looking towards the 'front' face of the N-Gon.
            camera.Position = new Point3D(0.0, 0.0, 3.0);

            //Looking down the Z-axis.
            camera.LookDirection = new Vector3D(0.0, 0.0, -1.0);

            camera.FieldOfView = 60.0;

            //Which way is up :)
            camera.UpDirection = yAxisVector;

            ViewportCamera = camera;
            
            //Register our camera so that animations can locate it by name.
            NameScope.GetNameScope(this).RegisterName("viewportCamera", ViewportCamera);

            //Assign our transform group to our camera and mark our viewport as needing a reset so the first time we try to animate the N-Gon (and thus
            //need our 3d view, we will set it up).
            this.viewportCamera.Transform = this.cameraTransformGroup;
            this.viewportNeedsReset = true;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Allows for adding some empty space where the n-gon faces meet, default value is 0.0.  Since the n-gon
        /// is laid out inside a unit circle values for this property likely lie in the range [0.0,1.0].
        /// </summary>
        public double FaceMargin
        {
            get
            {
                return (double)GetValue(NGonDisplayControl.FaceMarginProperty);
            }
            set
            {
                SetValue(NGonDisplayControl.FaceMarginProperty, value);
            }
        }

        /// <summary>
        /// Exposes the <see cref="UIElement"/> which is currently being displayed to the user.
        /// </summary>
        public UIElement CurrentItem
        {
            get
            {
                return (UIElement)GetValue(NGonDisplayControl.CurrentItemProperty);
            }
            set
            {
                SetValue(NGonDisplayControl.CurrentItemProperty, value);
            }
        }

        /// <summary>
        /// The collection of <see cref="Light"/> objects used to illuminate the viewport the NGonDisplayControl
        /// resides in.
        /// </summary>
        public IEnumerable<Light> ViewPortLights
        {
            get
            {
                return (IEnumerable<Light>)GetValue(NGonDisplayControl.ViewPortLightsProperty);
            }
            set
            {
                SetValue(NGonDisplayControl.ViewPortLightsProperty, value);
            }
        }

        /// <summary>
        /// The orientation of the NGonDisplayControl.  <see cref="Orientation.Horizontal"/> means that <see cref="UIElement">s</see> will be
        /// arranged circling the y-axis and displayed horizontally.  <see cref="Orientation.Vertical"/> means that <see cref="UIElement">s</see>
        /// will be arrange circling the x-axis and displayed vertically.
        /// </summary>
        public Orientation Orientation
        {
            get
            {
                return (Orientation)GetValue(NGonDisplayControl.OrientationProperty);
            }
            set
            {
                SetValue(NGonDisplayControl.OrientationProperty, value);
            }
        }

        /// <summary>
        /// Exposes the <see cref="TimeSpan"/> which represents the time the Zoom in and Zoom out animations should take to complete.
        /// </summary>
        public TimeSpan ZoomAnimationTime
        {
            get
            {
                return (TimeSpan)GetValue(NGonDisplayControl.ZoomAnimationTimeProperty);
            }
            set
            {
                SetValue(NGonDisplayControl.ZoomAnimationTimeProperty, value);
            }
        }

        /// <summary>
        /// Exposes the <see cref="TimeSpan"/> which represents the time the rotation animation should take to compelte.
        /// </summary>
        public TimeSpan RotationAnimationTime
        {
            get
            {
                return (TimeSpan)GetValue(NGonDisplayControl.RotationAnimationTimeProperty);
            }
            set
            {
                SetValue(NGonDisplayControl.RotationAnimationTimeProperty, value);
            }
        }

        /// <summary>
        /// Exposes the <see cref="ICollection{T}"/> of <see cref="UIElement">s</see> that the control is currently hosting.
        /// </summary>
        public ICollection<UIElement> Items
        {
            get
            {
                return (ICollection<UIElement>)GetValue(NGonDisplayControl.ItemsProperty);
            }
            set
            {
                SetValue(NGonDisplayControl.ItemsProperty, value);
            }
        }

        /// <summary>
        /// Determines whether the control can rotate forward or not.  Currently this is only false while animations are occuring, when we have
        /// no items (silly scenario), or when we have a single item (also a silly scenario).
        /// </summary>
        public bool CanRotateForward
        {
            get
            {
                return this.canRotateForward && (Items != null && Items.Count > 1);
            }
            internal set
            {
                this.canRotateForward = value;
                RaisePropertyChanged("CanRotateForward");
            }
        }

        /// <summary>
        /// Determines whether the control can rotate backwards or not.  Currently this is only false while animations are occuring, when we have
        /// no items (silly scenario) or when we only have a single item (also a silly scenario).
        /// </summary>
        public bool CanRotateBack
        {
            get
            {
                return this.canRotateBack && (Items != null && Items.Count > 1);
            }
            internal set
            {
                this.canRotateBack = value;
                RaisePropertyChanged("CanRotateBack");
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Animates from the currently displayed item to the next item in the items collection.  If <see cref="CanRotateForward"/> is returning
        /// <c>false</c> when this is called this method will do nothing.
        /// </summary>
        public void MoveNext()
        {
            if (CanRotateForward)
            {
                if (this.viewportNeedsReset)
                {
                    //Our viewport is dirty/uninitialized, so lets set it up now since we are about to use it.
                    ResetViewport();
                    InitializeViewport();

                    this.viewportNeedsReset = false;
                }

                //if face margin is > 0.0 then we will be able to see faces on the opposite side of the n-gon through the cracks.
                //in this case to maintain a correct look we need to ensure all faces have been created, otherwise we only need to
                //ensure the items along the path of rotation have been created.
                if (FaceMargin > 0.0)
                {
                    EnsurePathCalculated(Items.Count /*rotationCount*/ , RotationDirection.Forward /*rotationDirection*/ );
                }
                else
                {
                    EnsurePathCalculated(1/*rotationCount*/, RotationDirection.Forward /*rotationDirection*/);
                }

                this.currentFaceIndex = AdjustIndex(this.currentFaceIndex, RotationDirection.Forward);
                ApplyCameraAnimations(1 /*rotation multiplier*/, 1 /*rotation count*/);
            }
        }

        /// <summary>
        /// Animates from the currently displayed item to the next item in the items collection.  If <see cref="CanRotateBackwards"/> is returning
        /// <c>false</c> when this is called this method will do nothing.
        /// </summary>
        public void MovePrevious()
        {
            if (CanRotateBack)
            {
                if (this.viewportNeedsReset)
                {
                    //Our viewport is dirty/uninitialized, so lets set it up now since we are about to use it.
                    ResetViewport();
                    InitializeViewport();

                    this.viewportNeedsReset = false;
                }

                //if face margin is > 0.0 then we will be able to see faces on the opposite side of the n-gon through the cracks.
                //in this case to maintain a correct look we need to ensure all faces have been created, otherwise we only need to
                //ensure the items along the path of rotation have been created.
                if (FaceMargin > 0.0)
                {
                    EnsurePathCalculated(Items.Count /*rotationCount*/, RotationDirection.Backwards /*rotationDirection*/);
                }
                else
                {
                    EnsurePathCalculated(1 /*rotationCount*/, RotationDirection.Backwards /*rotationDirection*/);
                }

                this.currentFaceIndex = AdjustIndex(this.currentFaceIndex, RotationDirection.Backwards);
                ApplyCameraAnimations(-1 /*rotation multiplier*/, 1 /*rotation count*/);
            }
        }

        /// <summary>
        /// Animates from the currently displayed item to the item with the given identifier.  If no items with the given identifier exist or
        /// the shortest rotation path (forward/back) has its associated CanRotateX property return <c>false</c> this method will do nothing.
        /// </summary>
        public void MoveTo(string itemIdentifier)
        {
            if (itemIdentifier == null)
            {
                throw new ArgumentNullException("itemIdentifier");
            }

            if (Items != null)
            {
                UIElement item = Items.FirstOrDefault((i) => (string)i.GetValue(FrameworkElement.NameProperty) == itemIdentifier);
                if (item != null)
                {
                    MoveTo(item);
                    return;
                }

                Debug.WriteLine(String.Format("Request to rotate to face with id '{0}' failed.  No face with id '{0}' was found.", itemIdentifier));
            }
        }

        /// <summary>
        /// Animates from the currently displayed item to the given <see cref="UIElement"/> in the Items collection. If the given <see cref="UIElement"/> 
        /// is not found or the shortest rotation path (forward/back) our CanRotate(Forward/Back) property returns <c>false</c> this method will do nothing.
        /// </summary>
        private void MoveTo(UIElement item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            if (Items != null)
            {
                int currentPosition = this.currentFaceIndex;
                
                //BUGBUG:  A few places I need a List so I can index the collection, should either make the DP of type IList
                //or maintain a synchronized local copy of the DP provided ICollection
                List<UIElement> collection = new List<UIElement>(Items);

                //guard against doing any work in the degenerative do nothing case
                if (collection[currentPosition] != item)
                {
                    int newFaceIndex = -1;
                    int angleMultiplier;
                    int rotationCount;

                    //step 1 from our current position check the length of rotating forward through the items collection
                    //and backwards
                    int forwardRotationCount = 0;
                    int backwardsRotationCount = 0;

                    for (int i = ((currentPosition != collection.Count - 1) ? (currentPosition + 1) : 0); i != currentPosition; )
                    {
                        forwardRotationCount++;

                        if (collection[i] == item)
                        {
                            newFaceIndex = i;

                            if ((forwardRotationCount + this.currentFaceIndex) >= Items.Count)
                            {
                                backwardsRotationCount = this.currentFaceIndex - newFaceIndex;
                            }
                            else
                            {
                                backwardsRotationCount = (Items.Count - newFaceIndex) + this.currentFaceIndex;
                            }

                            break;
                        }

                        i = AdjustIndex(i, RotationDirection.Forward);
                    }

                    if (newFaceIndex != -1)
                    {
                        if (this.viewportNeedsReset)
                        {
                            ResetViewport();
                            InitializeViewport();

                            this.viewportNeedsReset = false;
                        }

                        //step 2:  pick the shorter of the two paths and set the proper angle multiplier and number of rotations
                        //we need to do to get to our target and ensure we are in a rotateable state
                        if (forwardRotationCount <= backwardsRotationCount)
                        {
                            if (!CanRotateForward)
                            {
                                return;
                            }

                            angleMultiplier = 1;
                            rotationCount = forwardRotationCount;
                        }
                        else
                        {
                            if (!CanRotateBack)
                            {
                                return;
                            }

                            angleMultiplier = -1;
                            rotationCount = backwardsRotationCount;
                        }

                        if (FaceMargin > 0.0)
                        {
                            EnsurePathCalculated(Items.Count /*rotationCount*/, (RotationDirection)angleMultiplier /*rotationDirection*/);
                        }
                        else
                        {
                            EnsurePathCalculated(rotationCount /*rotationCount*/, (RotationDirection)angleMultiplier /*rotationDirection*/);
                        }

                        //step 3:  change our face index to our destination and apply the animations to get us there
                        this.currentFaceIndex = newFaceIndex;
                        ApplyCameraAnimations(angleMultiplier, rotationCount);
                    }
                    else
                    {
                        string propertyIdentifier = "name";
                        string itemIdentifier = (string)item.GetValue(FrameworkElement.NameProperty);
                        if (itemIdentifier == null)
                        {
                            propertyIdentifier = "hashcode";
                            itemIdentifier = item.GetHashCode().ToString();
                        }

                        Debug.WriteLine(String.Format("Request to rotate to face with {0} '{1}' failed.  No face with {0} '{1}' was found.", propertyIdentifier, itemIdentifier));
                    }
                }
            }
        }

        #endregion

        #region Overridden Methods

        //since our mesh size/camera placement is dependent on the render size of our viewport we need to recalculate when that changes
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            this.viewportNeedsReset = true;
        }

        #endregion

        #region Private Helpers

        #region DependencyPropertyChanged Handlers

        /// <summary>
        /// DependencyPropertyChanged callback.  This alerts me when my <see cref="IEnumerable{T}"/> of <see cref="Light">s</see>
        /// that I am using has been replaced.
        /// </summary>
        private static void HandleLightCollectionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            ((NGonDisplayControl)sender).HandleLightCollectionChanged(args);
        }

        /// <summary>
        /// Instance version that the static DependencyPropertyChanged callback thunks into.
        /// </summary>
        private void HandleLightCollectionChanged(DependencyPropertyChangedEventArgs args)
        {
            if (args.OldValue != null)
            {
                INotifyCollectionChanged oldCollection = args.OldValue as INotifyCollectionChanged;

                if (oldCollection != null)
                {
                    oldCollection.CollectionChanged -= HandleLightCollectionChanged;
                }
            }

            RemoveLights();

            if (args.NewValue != null)
            {
                INotifyCollectionChanged newCollection = args.NewValue as INotifyCollectionChanged;

                if (newCollection != null)
                {
                    newCollection.CollectionChanged += HandleLightCollectionChanged;
                }

                AddLights();
            }
        }

        /// <summary>
        /// DependencyPropertyChanged callback.  This alerts me when the control's <see cref="CurrentItem"/> property has changed.
        /// </summary>
        private static void HandleCurrentItemPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            ((NGonDisplayControl)sender).HandleCurrentItemPropertyChanged(args);
        }

        /// <summary>
        /// Instance version that the static DependencyPropertyChanged callback thunks into.
        /// </summary>
        private void HandleCurrentItemPropertyChanged(DependencyPropertyChangedEventArgs args)
        {
            UIElement item = (UIElement)args.NewValue;

            if (item != null)
            {
                MoveTo(item);
            }
        }

        /// <summary>
        /// DependencyPropertyChanged callback.  This alerts me when my <see cref="ICollection{T}"/> of <see cref="UIElement">s</see>
        /// that I am hosting has been replaced.
        /// </summary>
        private static void HandleItemsPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            ((NGonDisplayControl)sender).HandleItemsPropertyChanged(args);
        }

        /// <summary>
        /// Instance version that the static DependencyPropertyChanged callback thunks into.
        /// </summary>
        private void HandleItemsPropertyChanged(DependencyPropertyChangedEventArgs args)
        {
            if (args.OldValue != null)
            {
                INotifyCollectionChanged oldCollection = args.OldValue as INotifyCollectionChanged;

                if (oldCollection != null)
                {
                    oldCollection.CollectionChanged -= HandleItemsCollectionChanged;
                }
            }

            this.viewportNeedsReset = true;

            if (args.NewValue != null)
            {
                INotifyCollectionChanged newCollection = args.NewValue as INotifyCollectionChanged;

                if (newCollection != null)
                {
                    newCollection.CollectionChanged += HandleItemsCollectionChanged;
                }

                if (this.currentFaceIndex >= Items.Count)
                {
                    this.currentFaceIndex = Items.Count - 1;
                }

                if (args.NewValue != null)
                {
                    if (Items.Count != 0)
                    {
                        List<UIElement> indexableList = new List<UIElement>(Items);
                        Content = CurrentItem = indexableList[this.currentFaceIndex];

                        CanRotateForward = CanRotateBack = (Items.Count > 1);
                    }
                }
                else
                {
                    Content = null;
                }
            }
            else
            {
                CanRotateForward = CanRotateBack = false;
            }
        }

        /// <summary>
        /// DependencyPropertyChanged callback.  This alerts me when the <see cref="Orientation"/> in which I am
        /// laying out my hosted <see cref="UIElement">s</see> has changed.
        /// </summary>
        private static void HandleOrientationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            ((NGonDisplayControl)sender).HandleOrientationChanged(args);
        }

        /// <summary>
        /// Instance version that the static DependencyPropertyChanged callback thunks into.
        /// </summary>
        private void HandleOrientationChanged(DependencyPropertyChangedEventArgs args)
        {
            ResetBrushOpacity();

            this.debuggingInfo.Add(String.Format("Orientation changed to '{0}', throwing away cached face models.", Orientation.ToString()));

            this.viewportNeedsReset = true;
        }

        /// <summary>
        /// DependencyPropertyChanged callback.  This alerts me when the face margin value has changed.
        /// </summary>
        private static void HandleFaceMarginChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            ((NGonDisplayControl)sender).HandleFaceMarginChanged(args);
        }

        /// <summary>
        /// Instance version that the static DependencyPropertyChanged callback thunks into.
        /// </summary>
        private void HandleFaceMarginChanged(DependencyPropertyChangedEventArgs args)
        {
            ResetBrushOpacity();

            this.viewportNeedsReset = true;
        }

        #endregion

        #region Private Properties

        #region Debugging Aids

        /// <summary>
        /// The collection of <see cref="VisualBrush"/> objects representing the elements the control is hosting.  This mainly used to be able to
        /// display all the elements the control is hosting for debugging purposes.
        /// </summary>
        public ObservableCollection<VisualBrush> Brushes
        {
            get
            {
                return this.brushes;
            }
        }

        /// <summary>
        /// Holds strings which are output at various points if debugging is enabled.
        /// </summary>
        public ObservableCollection<string> DebuggingInfo
        {
            get
            {
                return this.debuggingInfo;
            }
        }

        #endregion

        /// <summary>
        /// The camera used in the <see cref="ViewPort3D"/> that the <see cref="NGonDisplayControl"/> resides in.
        /// </summary>
        private PerspectiveCamera ViewportCamera
        {
            get
            {
                return this.viewportCamera;
            }
            set
            {
                this.viewportCamera = value;
            }
        }

        private bool[] CalculatedFaces
        {
            get
            {
                if (this.calculatedFaces == null)
                {
                    this.calculatedFaces = new bool[((Items != null) ? Items.Count : 4)];
                }

                return this.calculatedFaces;
            }
            set
            {
                this.calculatedFaces = value;
            }
        }

        #endregion

        /// <summary>
        /// Populates the <see cref="ObservableCollection{T}"/> of <see cref="VisualBrush"/> objects with a brush for each item
        /// we are hosting.  Assumes the visual brush collection is empty and adds all brushes with an opacity of 0.5.
        /// </summary>
        private void PopulateBrushCollection(IEnumerable<UIElement> items)
        {
            foreach (UIElement item in items)
            {
                VisualBrush brush = new VisualBrush();
                brush.AutoLayoutContent = false;
                brush.Opacity = 0.5;
                brush.Visual = item;

                this.brushes.Add(brush);
            }
        }

        /// <summary>
        /// Updates the brushes if <see cref="Items"/> is of type <see cref="INotifyCollectionChanged"/> and we receive a collection
        /// changed event.
        /// </summary>
        private void UpdateBrushCollection(NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    {
                        VisualBrush brush = new VisualBrush((UIElement)e.NewItems[0]);
                        brush.Opacity = 0.5;

                        this.brushes.Insert(e.NewStartingIndex, brush);
                        break;
                    }
                case NotifyCollectionChangedAction.Remove:
                    {
                        this.brushes.RemoveAt(e.OldStartingIndex);
                        break;
                    }
                case NotifyCollectionChangedAction.Replace:
                    {
                        VisualBrush brush = new VisualBrush((UIElement)e.NewItems[0]);
                        brush.Opacity = 0.5;

                        this.brushes.RemoveAt(e.OldStartingIndex);
                        this.brushes.Insert(e.OldStartingIndex, brush);
                        break;
                    }
                case NotifyCollectionChangedAction.Reset:
                    {
                        this.brushes.Clear();
                        break;
                    }
            }
        }

        /// <summary>
        /// Resets the Opacity of all <see cref="VisualBrush"/> objects to 0.5.  The opacity is used for debugging to indicate
        /// while items we are hosting have had 3d meshes calculated and placed in the view port.  If the opacity is 0.5 it means
        /// we haven't actually constructed the 3d representation, otherwise we have.
        /// </summary>
        private void ResetBrushOpacity()
        {
            foreach (VisualBrush brush in this.brushes)
            {
                brush.Opacity = 0.5;
            }
        }

        /// <summary>
        /// Creates the default lights that shine up our viewport.
        /// </summary>
        private static IEnumerable<Light> CreateDefaultLights()
        {
            List<Light> defaultLights = new List<Light>(6);

            //light shining down negative x-axis
            defaultLights.Add(new DirectionalLight(Colors.White, new Vector3D(-1.0, 0.0, 0.0)));

            //light shining up positive x-axis
            defaultLights.Add(new DirectionalLight(Colors.White, new Vector3D(1.0, 0.0, 0.0)));

            //light shining down negative y-axis
            defaultLights.Add(new DirectionalLight(Colors.White, new Vector3D(0.0, -1.0, 0.0)));

            //light shining up positive y-axis
            defaultLights.Add(new DirectionalLight(Colors.White, new Vector3D(0.0, 1.0, 0.0)));

            //light shining down negative z-axis
            defaultLights.Add(new DirectionalLight(Colors.White, new Vector3D(0.0, 0.0, -1.0)));

            //light shining up positive z-axis
            defaultLights.Add(new DirectionalLight(Colors.White, new Vector3D(0.0, 0.0, 1.0)));

            return defaultLights;
        }

        /// <summary>
        /// Clears old mesh data as well as clearing the list of faces we have calculated.
        /// </summary>
        private void ResetViewport()
        {
            for (int i = 0; i < ViewPort.Children.Count; i++)
            {
                ModelVisual3D model = ViewPort.Children[i] as ModelVisual3D;

                //we only want to remove the 3d models that are hosting UIElements, not the lights
                if (!(model.Content is Light))
                {
                    //readjust our index so we don't skip any visuals
                    ViewPort.Children.RemoveAt(i--);
                }
            }

            //since all the meshes are gone we need to recalculate all faces on request
            Array.Clear(CalculatedFaces, 0, CalculatedFaces.Length);

            this.brushes.Clear();
        }

        /// <summary>
        /// Goes about constructing the n-gon which hosts the UIElements.  Calculates the vertex points for the n-gon
        /// and makes sure the face that is facing the camera is constructed and visible.
        /// </summary>
        private void InitializeViewport()
        {
            if (Items == null || Items.Count == 0 || ActualWidth == 0.0 || ActualHeight == 0.0)
            {
                return;
            }

            //first time laying out our visuals, we have no lights yet so lets put them in first
            if (!this.lightsAdded)
            {
                AddLights();
                this.lightsAdded = true;
            }

            if (this.currentFaceIndex >= Items.Count)
            {
                this.currentFaceIndex = Items.Count - 1;
            }

            if (CalculatedFaces.Length < Items.Count)
            {
                CalculatedFaces = new bool[Items.Count];
            }
            else
            {
                Array.Clear(CalculatedFaces, 0, CalculatedFaces.Length);
            }

            PopulateBrushCollection(Items);

            //calculate our n-gons vertex points, these will be used to place the meshes initially before they are rotated
            //into final viewing position
            double sideLength = (Orientation == Orientation.Horizontal) ? ActualWidth / ActualHeight : 1.0;
            this.vertexPoints = NGon3DHelper.CalculateVertexPoints(Items.Count, sideLength, FaceMargin, Orientation);

            EnsureFaceCalculated(this.currentFaceIndex);

            this.brushes[this.currentFaceIndex].Opacity = 1.0;

            ResetCameraPosition();
        }

        /// <summary>
        /// Remove all <see cref="Light"/> objects from the <see cref="ViewPort3D"/> the <see cref="NGonDisplayControl"/>
        /// is hosted in.
        /// </summary>
        private void RemoveLights()
        {
            for (int i = 0; i < ViewPort.Children.Count; i++)
            {
                ModelVisual3D model = ViewPort.Children[i] as ModelVisual3D;
                if (model.Content is Light)
                {
                    ViewPort.Children.RemoveAt(i--);
                }
            }
        }

        /// <summary>
        /// Adds directional lights to the view port so we can see stuff
        /// </summary>
        private void AddLights()
        {
            foreach (Light light in ViewPortLights)
            {
                ModelVisual3D lightSource = new ModelVisual3D();
                lightSource.Content = light;
                ViewPort.Children.Add(lightSource);
            }
        }

        /// <summary>
        /// Takes off any rotations that are currently applied to the camera and resets to be looking at the current face.
        /// </summary>
        private void ResetCameraPosition()
        {
            Vector3D rotationVector = (Orientation == Orientation.Horizontal) ? yAxisVector : xAxisVector;

            //this allows us to reset the animation state of the quaternion, otherwise our set on the next
            //line has no effect
            this.cameraAngleRotation.BeginAnimation(QuaternionRotation3D.QuaternionProperty, null);
            this.cameraAngleRotation.Quaternion = new Quaternion(rotationVector, 0.0);

            if (ViewportCamera.IsFrozen)
            {
                ViewportCamera = ViewportCamera.Clone();
            }

            ViewportCamera.UpDirection = this.cameraUpDirection;
            ViewportCamera.Position = this.cameraPosition;
            ViewportCamera.LookDirection = this.faceNormal;
        }

        /// <summary>
        /// Applies the zoom / rotation animations.
        /// </summary>
        /// <param name="angleMultiplier">Determines if the rotation should be forwards or backwards through the item collection.  -1 means backwards, 1 means forwards.</param>
        /// <param name="rotationCount"></param>
        private void ApplyCameraAnimations(int angleMultiplier, int rotationCount)
        {
            Content = ViewPort;

            if (angleMultiplier != -1 && angleMultiplier != 1)
            {
                throw new ArgumentException("Value must be either -1 or 1.", "angleMultiplier");
            }

            Storyboard cameraAnimationStoryBoard = new Storyboard();

            double rotationAngle = ((360.0 / Items.Count) * rotationCount) * angleMultiplier;
            Vector3D axisOfRotation = (Orientation == Orientation.Horizontal) ? yAxisVector : xAxisVector;

            AnimationHelper.AddZoomAnimations(cameraAnimationStoryBoard, ZoomAnimationTime, TimeSpan.Zero, 1.5);

            double rotationTimeInMilliseconds = RotationAnimationTime.TotalMilliseconds;
            rotationTimeInMilliseconds *= rotationCount;

            AnimationHelper.AddRotationAnimation(cameraAnimationStoryBoard, TimeSpan.FromMilliseconds(rotationTimeInMilliseconds), ZoomAnimationTime,
                                                 rotationAngle, axisOfRotation, this.cameraAngleRotation.Quaternion);

            double finalZoomStartTime = ZoomAnimationTime.TotalMilliseconds + rotationTimeInMilliseconds;
            AnimationHelper.AddZoomAnimations(cameraAnimationStoryBoard, ZoomAnimationTime,
                                              TimeSpan.FromMilliseconds(finalZoomStartTime), 1.0);

            CanRotateForward = CanRotateBack = false;
            
            cameraAnimationStoryBoard.Completed += delegate
                                                   {                                                       
                                                       CanRotateBack = CanRotateForward = true;

                                                       List<UIElement> indexableList = new List<UIElement>(Items);
                                                       CurrentItem = indexableList[this.currentFaceIndex];

                                                       Content = indexableList[this.currentFaceIndex];
                                                   };

            cameraAnimationStoryBoard.Begin(this);
        }

        /// <summary>
        /// Called when our Items collection is an <see cref="ObservableCollection{T}"/> and we have been notified of
        /// some membership change.
        /// </summary>
        private void HandleItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateBrushCollection(e);

            //Unfortunately adding and removing from the collection causes us to need to redo the n-gon arrangement
            //since a side has been added/disappeared, so we can't really avoid doing a whole calculate locations/place pass.
            this.viewportNeedsReset = true;

            CanRotateForward = CanRotateBack = (Items.Count > 1);
        }

        /// <summary>
        /// Called when our Light collection is an <see cref="ObservableCollection{T}"/> and we have been notified of
        /// some membership change.
        /// </summary>
        private void HandleLightCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                //not quite sure how to best handle replace and remove since for remove it doesn't appear
                //to tell you what was removed (NotifyCollectionChangedEventArgs.OldItems has a doc comment
                //that appears to indicate it is for the Replace case).  For now just lump them all together
                case NotifyCollectionChangedAction.Replace:
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Reset:
                    {
                        RemoveLights();
                        AddLights();
                        break;
                    }
                case NotifyCollectionChangedAction.Add:
                    {
                        foreach (Light newLight in e.NewItems)
                        {
                            ModelVisual3D v3d = new ModelVisual3D();
                            v3d.Content = newLight;
                            ViewPort.Children.Add(v3d);
                        }

                        break;
                    }
            }
        }

        /// <summary>
        /// A simple enum to indicate which way to rotate, used mainly when adjusting collection indices and calculating which faces need
        /// to be visible prior to performing a transition.
        /// </summary>
        private enum RotationDirection
        {
            Forward = 1,
            Backwards = -1
        }

        /// <summary>
        /// Adjusts the given index based on the given rotation direction handling wrapping the count to make sure it is never less than
        /// 0 or greater than (Items.Count-1)
        /// </summary>
        private int AdjustIndex(int index, RotationDirection rotationDirection)
        {
            int newIndex;

            if (rotationDirection == RotationDirection.Forward)
            {
                newIndex = (index + 1) % Items.Count;
            }
            else
            {
                newIndex = (index != 0) ? index - 1 : Items.Count - 1;
            }

            return newIndex;
        }

        /// <summary>
        /// Ensures that the 3d visual for the given face index has been calculated and placed into the view port.  If the face index
        /// supplied also happens to be the current face index this method will also set the camera up to be in the proper position to
        /// view it (if necessary).
        /// </summary>
        private void EnsureFaceCalculated(int faceIndex)
        {
            if (!CalculatedFaces[faceIndex])
            {
                double aspectRatio = ActualWidth / ActualHeight;

                //BUGBUG:  There are a couple points I need an indexable collection, should either maintain local copy of Items collection 
                //as List<T> or have the DP type be IList<T>
                List<UIElement> indexableList = new List<UIElement>(Items);

                ModelVisual3D modelVisual3D;

                if (faceIndex != currentFaceIndex)
                {
                    Size availableSize = new Size(ActualWidth, ActualHeight);
                    Rect arrangeRect = new Rect(availableSize);

                    indexableList[faceIndex].Measure(availableSize);
                    indexableList[faceIndex].Arrange(arrangeRect);                    
                }

                if (Items.Count > 1)
                {
                    Point3D vertexPoint = this.vertexPoints[faceIndex];
                    Point3D nextVertexPoint = (faceIndex != this.vertexPoints.Length - 1) ? this.vertexPoints[faceIndex + 1] : this.vertexPoints[0];

                    modelVisual3D = NGon3DHelper.CreateModelVisual(vertexPoint, nextVertexPoint, Orientation, indexableList[faceIndex], aspectRatio, ((FaceMargin > 0.0) && indexableList.Count > 2));
                }
                else
                {
                    //There is no placement/rotation in this case, we can just leave it at (0,0,0)
                    modelVisual3D = NGon3DHelper.CreateModelVisual(Transform3DGroup.Identity, NGon3DHelper.CreateFaceMesh(aspectRatio), indexableList[faceIndex], false);
                }

                ViewPort.Children.Add(modelVisual3D);

                //we also need to calculate the camera info since we will be looking at this face
                if (faceIndex == this.currentFaceIndex)
                {
                    Transform3D modelTransform = modelVisual3D.Transform;
                    Vector3D u = modelTransform.Transform(yAxisVector);
                    Vector3D v = modelTransform.Transform(xAxisVector);

                    //these fields are used by ResetCameraPosition, which should be being called later if we are constructing
                    //the visible mesh
                    this.faceNormal = Vector3D.CrossProduct(u, v);

                    Point3D initialCameraPosition = NGon3DHelper.CalculateInitialCameraPosition(aspectRatio);
                    this.cameraPosition = modelTransform.Transform(initialCameraPosition);

                    //re-use the already transformed y-axis vector we used to calculate the face normal
                    this.cameraUpDirection = u;
                }

                //make the visual brush opaque since we have now actually constructed this side
                this.brushes[faceIndex].Opacity = 1.0;

                this.debuggingInfo.Add(String.Format("Caclucated face model for index '{0}'.", faceIndex.ToString()));
                CalculatedFaces[faceIndex] = true;
            }
        }

        /// <summary>
        /// Ensures that every member along a rotation path has had its mesh face constructed and placed in the viewport.
        /// </summary>
        private void EnsurePathCalculated(int rotationCount, RotationDirection rotationDirection)
        {
            //start ensuring visibility on the face after the current one in the direction of rotation
            int current = AdjustIndex(this.currentFaceIndex, rotationDirection);
            for (int i = 0; i < rotationCount; i++)
            {
                EnsureFaceCalculated(current);
                current = AdjustIndex(current, rotationDirection);
            }

            //for n-gons greater than 4 sides we need to ensure that the faces on both sides of the current face are calculated 
            //even though we are only rotating in one direction.  Otherwise you get a weird view on that missing side since it is
            //partially visible when we zoom out, we also need to do the same thing for the target face so its "next" side
            //isn't missing
            if (Items.Count > 4)
            {
                EnsureSurroundingFacesCalculated(this.currentFaceIndex, rotationDirection);
                EnsureFaceCalculated(current);
            }
        }

        /// <summary>
        /// Ensures the faces on either side of the given index (as well as the given index face) are calculated and in the view port.
        /// </summary>
        /// <remarks>If the n-gon we have laid out is under 5 sides then this method will only ensure the given face index and either
        /// the next or previous face (depending on rotation direction) are calculated.  If the n-gon has more than five side it will
        /// ensure the both sides of the given face index have been calculated.</remarks>
        private void EnsureSurroundingFacesCalculated(int faceIndex, RotationDirection rotationDirection)
        {
            int previousIndex = AdjustIndex(faceIndex, RotationDirection.Backwards);
            int nextIndex = AdjustIndex(faceIndex, RotationDirection.Forward);

            EnsureFaceCalculated(faceIndex);

            if (Items.Count < 5)
            {
                EnsureFaceCalculated((rotationDirection == RotationDirection.Forward) ? nextIndex : previousIndex);
            }
            else
            {
                EnsureFaceCalculated(previousIndex);
                EnsureFaceCalculated(nextIndex);
            }
        }

        /// <summary>
        /// Helper method to raise property change notifications.
        /// </summary>
        private void RaisePropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private Viewport3D ViewPort
        {
            get
            {
                if (this.viewport3D == null)
                {
                    this.viewport3D = new Viewport3D();
                    this.viewport3D.Camera = this.viewportCamera;
                }

                return this.viewport3D;
            }
        }

        #endregion

        #region INotifyPropertyChanged Members

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion
    }
}