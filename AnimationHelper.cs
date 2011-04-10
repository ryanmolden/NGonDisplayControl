namespace Demo
{
    using System;
    using System.Windows;
    using System.Windows.Media.Animation;
    using System.Windows.Media.Media3D;

    internal static class AnimationHelper
    {
        internal static void AddZoomAnimations(Storyboard storyboard, TimeSpan animationTime, TimeSpan beginTime, double scaleFactor)
        {
            DependencyProperty[] targets = new DependencyProperty[] { ScaleTransform3D.ScaleXProperty, ScaleTransform3D.ScaleYProperty, ScaleTransform3D.ScaleZProperty };

            for (int i = 0; i < targets.Length; i++)
            {
                DoubleAnimation zoomAnimation = new DoubleAnimation(scaleFactor, new Duration(animationTime));
                zoomAnimation.BeginTime = beginTime;

                Storyboard.SetTargetName(zoomAnimation, "viewportCamera");

                //Property path for the animation target is going to be the Transform property's Children collection. Specifically the item at index 0, 
                //which is our scaletransform, and on it we want to animate the X, Y and Z properties...whew, that was confusing :)
                PropertyPath path = new PropertyPath("(0).(1)[0].(2)", PerspectiveCamera.TransformProperty, Transform3DGroup.ChildrenProperty, targets[i]);
                Storyboard.SetTargetProperty(zoomAnimation, path);

                storyboard.Children.Add(zoomAnimation);
            }
        }

        internal static void AddRotationAnimation(Storyboard storyboard, TimeSpan animationTime, TimeSpan beginTime, double angleOfRotation, Vector3D axisOfRotation, Quaternion currentRotationQuaternion)
        {
            Quaternion delta = new Quaternion(axisOfRotation, angleOfRotation);
            Quaternion newRotation = currentRotationQuaternion * delta;

            QuaternionAnimation rotationAnimation = new QuaternionAnimation(newRotation, new Duration(animationTime));
            rotationAnimation.AccelerationRatio = 0.5;
            rotationAnimation.DecelerationRatio = 0.5;
            rotationAnimation.BeginTime = beginTime;

            Storyboard.SetTargetName(rotationAnimation, "viewportCamera");

            //Property path for the animation target is going to be the Transform property's Children collection. Specifically the item at index 1 (the first item is the zoom transform), 
            //which is our rotation transform, and on it we want to animate the Rotation property's Quarternion property...whew, that was confusing :)
            PropertyPath path = new PropertyPath("(0).(1)[1].(2).(3)", PerspectiveCamera.TransformProperty, Transform3DGroup.ChildrenProperty, RotateTransform3D.RotationProperty, QuaternionRotation3D.QuaternionProperty);
            Storyboard.SetTargetProperty(rotationAnimation, path);

            storyboard.Children.Add(rotationAnimation);
        }
    }
}