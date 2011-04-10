namespace Demo
{
    using System;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using System.Windows.Media.Media3D;

    internal static class NGon3DHelper
    {
        private static double lastAspectRatio;
        private static MeshGeometry3D lastCalculatedMeshGeometry;

        private static readonly Vector3D xAxisVector = new Vector3D(1.0, 0.0, 0.0);
        private static readonly Vector3D yAxisVector = new Vector3D(0.0, 1.0, 0.0);

        private const double RadiansToDegrees = 180.0 / Math.PI;
        private const double DegreesToRadians = Math.PI / 180.0;

        /// <summary>
        /// Calculates a placement vector which is currently just the midpoint between the two given points.
        /// </summary>
        internal static Vector3D CalculatePlacementVector(Point3D point1, Point3D point2)
        {
            return new Vector3D((point1.X + point2.X) / 2.0, (point1.Y + point2.Y) / 2.0, (point1.Z + point2.Z) / 2.0);
        }

        /// <summary>
        /// Creates a <see cref="MeshGeometry3D"/> representing a plane which is big enough to completely fill the viewport based on
        /// our view port size (and how we position the camera).
        /// </summary>
        internal static MeshGeometry3D CreateFaceMesh(double aspectRatio)
        {
            //only create one mesh per aspect ratio since we can reuse MeshGeometry3D objects ad nauseum
            if (lastAspectRatio != aspectRatio || lastCalculatedMeshGeometry == null)
            {
                lastAspectRatio = aspectRatio;

                //create our dynamically sized mesh, just big enough to fill the viewport
                lastCalculatedMeshGeometry = new MeshGeometry3D();
                lastCalculatedMeshGeometry.Positions.Add(new Point3D(-aspectRatio / 2.0, 0.5, 0.0));
                lastCalculatedMeshGeometry.Positions.Add(new Point3D(-aspectRatio / 2.0, -0.5, 0.0));
                lastCalculatedMeshGeometry.Positions.Add(new Point3D(aspectRatio / 2.0, -0.5, 0.0));
                lastCalculatedMeshGeometry.Positions.Add(new Point3D(aspectRatio / 2.0, 0.5, 0.0));
                lastCalculatedMeshGeometry.TextureCoordinates.Add(new Point(0.0, 0.0));
                lastCalculatedMeshGeometry.TextureCoordinates.Add(new Point(0.0, 1.0));
                lastCalculatedMeshGeometry.TextureCoordinates.Add(new Point(1.0, 1.0));
                lastCalculatedMeshGeometry.TextureCoordinates.Add(new Point(1.0, 0.0));
                lastCalculatedMeshGeometry.TriangleIndices.Add(0);
                lastCalculatedMeshGeometry.TriangleIndices.Add(1);
                lastCalculatedMeshGeometry.TriangleIndices.Add(2);
                lastCalculatedMeshGeometry.TriangleIndices.Add(0);
                lastCalculatedMeshGeometry.TriangleIndices.Add(2);
                lastCalculatedMeshGeometry.TriangleIndices.Add(3);
            }

            return lastCalculatedMeshGeometry;
        }

        /// <summary>
        /// Calculates vertex points for our n-gon.
        /// </summary>
        internal static Point3D[] CalculateVertexPoints(int sideCount, double sideLength, double faceMargin, Orientation orientation)
        {
            Point3D[] vertexPoints = new Point3D[sideCount];

            //radius of circumscribing circle = (1/2)*sideLength*csc(180/sideCount)
            //
            //The faceMargin is a way for users to request a little gap between faces if they so desire, the default value is 0.0
            double circumscribingCircleRadius = (0.5 * sideLength) / Math.Sin((Math.PI / sideCount)) + faceMargin;

            //I use polar coordinates for ease of circumnavigation then convert back to cartesian for rendering in WPF
            double theta;
            double radians;
            theta = radians = 0.0;

            //we find vertex points simply by cutting our circle into sideCount pieces
            double angleIncrement = 360.0 / sideCount;

            for (int i = 0; i < sideCount; i++)
            {
                if (orientation == Orientation.Horizontal)
                {
                    //negate the z-coordinate since we are basically looking down on the XZ plane and from the perspective
                    //-z is "up"
                    vertexPoints[i] = new Point3D(circumscribingCircleRadius * Math.Cos(radians), 0.0, -circumscribingCircleRadius * Math.Sin(radians));
                }
                else
                {
                    vertexPoints[i] = new Point3D(0.0, circumscribingCircleRadius * Math.Sin(radians), -circumscribingCircleRadius * Math.Cos(radians));
                }

                theta += angleIncrement;
                radians = theta * DegreesToRadians;
            }

            return vertexPoints;
        }

        /// <summary>
        /// Calculates the proper rotation angle to get a mesh face drawn at the origin (then translated) to be placed on the line connecting
        /// the two given points (two vertices in our n-gon).
        /// </summary>
        internal static double CalculateRotationAngle(Point3D point1, Point3D point2, Orientation orientation)
        {
            double rotationAngle;

            //Horrible math alert.  Basically the vertex to vertex line forms the hypotenuse of a right triangle
            //with our mesh face.  So to figure out how much we need to rotate the face by we simply need to do some
            //trig.  Since tan = opp / adj and that ratio also happens to be the slope of the line that forms the
            //hypotenuse we know that tan(theta) = z2-z1 / x2-x1.  Therefore theta = arctan(z2-z1/x2-x1).  
            if (orientation == Orientation.Horizontal)
            {
                //The choice to make the rotation angle negative is equivalent to taking the complement of the angle we
                //calculated, both would work.
                rotationAngle = -Math.Atan2(point2.Z - point1.Z, point2.X - point1.X) * RadiansToDegrees;
            }
            else
            {
                //the rise of the line in this case is actually the change in z coordinates which is why it is plugged
                //in for the 'y' value.  Using the complement here is to ensure the faces are facing the right direction
                //(towards the camera).  Drawing some of these cases out makes it easier to understand the seemingly wacky looking
                //choices here.
                rotationAngle = -(180.0 - Math.Atan2(point2.Z - point1.Z, point2.Y - point1.Y) * RadiansToDegrees);
            }

            return rotationAngle;
        }

        /// <summary>
        /// Calculates the cameras placement assuming we are looking at the origin down the Z axis and that the FOV is 60 degrees 
        /// (this position will be transformed as appropriate to position the camera facing whatever face we happen to be looking at).
        /// </summary>
        internal static Point3D CalculateInitialCameraPosition(double aspectRatio)
        {
            //Basically the camera's frustrum, when we are looking down at the camera along the y-axis, is a triangle with 
            //our mesh as the base like so (camera is the asterisk, look direction is towards mesh)
            //
            //                (mesh)
            //              ---------
            //              \   |   /
            //               \  |  /
            //                \ | /
            //                 \|/
            //                  *
            //
            //We know the camera viewing angle is 60 degrees, and we know the length of the mesh is ActualWidth/ActualHeight
            //The proper initial Z-position is (ActualWidth/ActualHeight)/(2*tan(30)) 
            return new Point3D(0.0, 0.0, aspectRatio / (2 * Math.Tan(30 * DegreesToRadians)));
        }

        /// <summary>
        /// Creates a mesh face that will be translated and rotated to lie on the line containing the two vertex points and will be painted
        /// with the given visual.
        /// </summary>
        internal static ModelVisual3D CreateModelVisual(Point3D vertexPoint1, Point3D vertexPoint2, Orientation orientation, Visual visual, double aspectRatio, bool enableBackFaceVisibility)
        {
            Vector3D placementVector = NGon3DHelper.CalculatePlacementVector(vertexPoint1, vertexPoint2);
            Point3D rotationCenter = new Point3D(placementVector.X, placementVector.Y, placementVector.Z);
            double rotationAngle = NGon3DHelper.CalculateRotationAngle(vertexPoint1, vertexPoint2, orientation);

            Transform3DGroup transformGroup = new Transform3DGroup();

            Vector3D rotationAxis = (orientation == Orientation.Horizontal) ? yAxisVector : xAxisVector;

            //translate and rotate the mesh face so it is in its proper position in the n-gon
            transformGroup.Children.Add(new TranslateTransform3D(placementVector));
            transformGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(rotationAxis, rotationAngle), rotationCenter));

            MeshGeometry3D mesh = NGon3DHelper.CreateFaceMesh(aspectRatio);
            return NGon3DHelper.CreateModelVisual(transformGroup, mesh, visual, enableBackFaceVisibility);
        }

        /// <summary>
        /// Creates a <see cref="ModelVisual3D"/> using the given <see cref="Transform3D"/>, <see cref="MeshGeometry"/> and <see cref="Visual"/>.
        /// </summary>
        internal static ModelVisual3D CreateModelVisual(Transform3D transform, MeshGeometry3D mesh, Visual element, bool enableBackFaceVisibility)
        {
            ModelVisual3D modelVisual3D = new ModelVisual3D();
            modelVisual3D.Transform = transform;

            GeometryModel3D gm3d = new GeometryModel3D();
            gm3d.Geometry = mesh;

            VisualBrush visualBrush = CreateVisualBrush(element);

            DiffuseMaterial material = new DiffuseMaterial();
            material.Brush = visualBrush;

            gm3d.Material = material;

            if (enableBackFaceVisibility)
            {
                gm3d.BackMaterial = material;
            }

            modelVisual3D.Content = gm3d;

            return modelVisual3D;
        }

        /// <summary>
        /// Creates a <see cref="VisualBrush"/> that can paint the given <see cref="Visual"/>.
        /// </summary>
        private static VisualBrush CreateVisualBrush(Visual visual)
        {
            VisualBrush visualBrush = new VisualBrush();
            visualBrush.AutoLayoutContent = false;
            visualBrush.Visual = visual;
            RenderOptions.SetCachingHint(visualBrush, CachingHint.Cache);

            return visualBrush;
        }
    }
}