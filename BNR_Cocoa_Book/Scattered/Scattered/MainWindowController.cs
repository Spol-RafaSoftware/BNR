﻿using System;

using Foundation;
using AppKit;
using CoreAnimation;
using CoreGraphics;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Scattered
{
    public partial class MainWindowController : NSWindowController
    {
		#region - Member Variables / Properties
		CALayer textContainer;
		CATextLayer textLayer;
		Random random;
		CGSize lastWindowSize;
		bool windowIsResizing = false;

		public new MainWindow Window
		{
			get { return (MainWindow)base.Window; }
		}
		#endregion

		#region - Constructors
        public MainWindowController(IntPtr handle) : base(handle)
        {
			Initialize();
        }

        [Export("initWithCoder:")]
        public MainWindowController(NSCoder coder) : base(coder)
        {
			Initialize();
        }

        public MainWindowController() : base("MainWindow")
        {
			Initialize();
        }

		void Initialize()
		{
			random = new Random((int)DateTime.Now.Ticks);
		}

		#endregion

		#region - Lifecycle
        public override void AwakeFromNib()
        {
            base.AwakeFromNib();

			View.Layer = new CALayer();
			View.WantsLayer = true;

			textContainer = new CALayer();
			textContainer.AnchorPoint = CGPoint.Empty;
			textContainer.Position = new CGPoint(10, 10);
			textContainer.ZPosition = 100;
			textContainer.BackgroundColor = NSColor.Black.CGColor;
			textContainer.BorderColor = NSColor.White.CGColor;
			textContainer.BorderWidth = 2;
			textContainer.CornerRadius = 15;
			textContainer.ShadowOpacity = 0.5f;
			View.Layer.AddSublayer(textContainer);

			textLayer = new CATextLayer();
			textLayer.AnchorPoint = CGPoint.Empty;
			textLayer.Position = new CGPoint(10, 6);
			textLayer.ZPosition = 100;
			textLayer.FontSize = 24;
			textLayer.ForegroundColor = NSColor.White.CGColor;
			textContainer.AddSublayer(textLayer);

			// Rely on setText: to set the above layers' bounds: [self setText:@"Loading..."];
			SetText("Loading...", textLayer);


			var dirs = NSSearchPath.GetDirectories(NSSearchPathDirectory.LibraryDirectory, NSSearchPathDomain.Local);
			foreach (string dir in dirs) {
				Console.WriteLine("Dir: {0}", dir);
			}
			string libDir = dirs[0];
			string desktopPicturesDir = Path.Combine(libDir, "Desktop Pictures");
			Console.WriteLine("DP Dir: {0}", desktopPicturesDir);

			// Launch loading of images on background thread
			Task.Run(async () => {
				await AddImagesFromFolderUrlAsync(desktopPicturesDir);
			});

			repositionButton.Layer.ZPosition = 100;
			durationTextField.Layer.ZPosition = 100;
			lastWindowSize = Window.Frame.Size;

			Window.DidResize += (sender, e) => {
				if (Math.Abs(lastWindowSize.Width - Window.Frame.Width) > 25 || Math.Abs(lastWindowSize.Height - Window.Frame.Height) > 25) {
					windowIsResizing = true;
					repositionImages(repositionButton);
					lastWindowSize = Window.Frame.Size;
					windowIsResizing = false;
				}
			};

        }
			
		#endregion

		#region - Actions
		partial void repositionImages (NSObject sender)
		{
			foreach (CALayer layer in View.Layer.Sublayers) {
				if (layer == textContainer || layer == repositionButton.Layer || layer == durationTextField.Layer)
					continue;

				CGRect imageBounds = layer.Bounds;

				nfloat X, Y = 0;
				if (windowIsResizing) {
					X = layer.Position.X + ((layer.Position.X - imageBounds.Size.Width/2)/(lastWindowSize.Width - imageBounds.Size.Width) *  (Window.Frame.Width - lastWindowSize.Width));
					Y = layer.Position.Y + ((layer.Position.Y - imageBounds.Size.Height/2)/(lastWindowSize.Height - imageBounds.Size.Height) *  (Window.Frame.Height - lastWindowSize.Height));
				}
				else {
					X = (nfloat)random.Next((int)Math.Floor(imageBounds.Width/2), (int)Math.Floor(layer.SuperLayer.Bounds.GetMaxX() - imageBounds.Width/2));
					Y = (nfloat)random.Next((int)Math.Floor(imageBounds.Height/2), (int)Math.Floor(layer.SuperLayer.Bounds.GetMaxY() - imageBounds.Height/2));
				}
				CGPoint newPoint = new CGPoint(X, Y);

				CAMediaTimingFunction tr = CAMediaTimingFunction.FromName(CAMediaTimingFunction.Linear);
				CAMediaTimingFunction tf = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseInEaseOut);
				CABasicAnimation posAnim = CABasicAnimation.FromKeyPath("position");
				posAnim.From = NSValue.FromCGPoint(layer.Position);
				posAnim.Duration = windowIsResizing ? 0 : durationTextField.FloatValue;
				posAnim.TimingFunction = windowIsResizing ? tr : tf;

				CABasicAnimation zPosAnim = CABasicAnimation.FromKeyPath("zPosition");
				zPosAnim.From = NSNumber.FromDouble(layer.ZPosition);
				zPosAnim.Duration = durationTextField.FloatValue;
				zPosAnim.TimingFunction = tf;

				layer.Actions = NSDictionary.FromObjectsAndKeys(new NSObject[]{posAnim, zPosAnim}, new NSObject[]{new NSString("position"), new NSString("zPosition")});

				CATransaction.Begin();
				layer.Position = newPoint;
				if (!windowIsResizing)
					layer.ZPosition = random.Next(-100, 99);
				CATransaction.Commit();
			}
		}
		#endregion

		#region - Methods
		async Task AddImagesFromFolderUrlAsync(string folderUrl)
		{
			DateTime t0 = DateTime.Now; 
			IEnumerable<string> dir = Directory.EnumerateFiles(folderUrl);

			foreach (string file in dir) {
				if (!file.EndsWith(".jpg", StringComparison.InvariantCultureIgnoreCase))
					continue;
				// Need to load image from file into NSData because you need the main thread to set an image for NSImage
				// No matter how you set the image as far as I can tell. 
				NSData data = await LoadImageDataFromFileAsync(file);
				if (data == null)
					continue;
				
				Console.WriteLine("Loaded Image: {0}", file);

				// BeginOInvokeOnMainThread is blocking, InvokeOnMainThread is not.
				InvokeOnMainThread(async () => {
					// As noted above, needs to run on Main Thread.
					NSImage image = new NSImage(data);

					// Time consuming task
					NSImage thumbImage = await ThumbImageFromImageAsync(image);

					PresentImage(thumbImage, file.Substring(file.LastIndexOf("/")+1));
					SetText(String.Format("{0}", DateTime.Now - t0), textLayer);

				});
			}
		}

		//- (NSImage *)thumbImageFromImage:(NSImage *)image;
		async Task<NSImage> ThumbImageFromImageAsync(NSImage image)
		{
			nfloat targetHeight = 200.0f;
			CGSize imageSize = image.Size;
			CGSize smallerSize = new CGSize(targetHeight * imageSize.Width / imageSize.Height, targetHeight);

			NSImage smallerImage = new NSImage(smallerSize);

			smallerImage.LockFocus();
			image.DrawInRect(new CGRect(0,0,smallerSize.Width, smallerSize.Height), CGRect.Empty, NSCompositingOperation.Copy, 1.0f);
			smallerImage.UnlockFocus();

			return smallerImage;
		}

		//- (void)presentImage:(NSImage *)image;
		void PresentImage(NSImage image, string filename)
		{
			int animationSpeed = 3;

			CGRect superLayerBounds = View.Layer.Bounds;
			CGPoint center = new CGPoint(superLayerBounds.GetMidX(), superLayerBounds.GetMidY());

			CGRect imageBounds = new CGRect(0, 0, image.Size.Width, image.Size.Height);

			nfloat X = (nfloat)random.Next((int)Math.Floor(imageBounds.Width/2), (int)Math.Floor(superLayerBounds.GetMaxX() - imageBounds.Width/2));//(superLayerBounds.GetMaxX() - imageBounds.Width/2) * random.NextDouble();
			nfloat Y = (nfloat)random.Next((int)Math.Floor(imageBounds.Height/2), (int)Math.Floor(superLayerBounds.GetMaxY() - imageBounds.Height/2)); //(superLayerBounds.GetMaxY() - imageBounds.Height/2) * random.NextDouble();
			CGPoint randomPoint = new CGPoint(X, Y);

			CAMediaTimingFunction tf = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseInEaseOut);

			// Animations for image layer
			CABasicAnimation posAnim = CABasicAnimation.FromKeyPath("position");
			posAnim.From = NSValue.FromCGPoint(center);
			posAnim.Duration = animationSpeed;
			posAnim.TimingFunction = tf;

			CABasicAnimation bdsAnim = CABasicAnimation.FromKeyPath("bounds");
			bdsAnim.From = NSValue.FromCGRect(CGRect.Empty);
			bdsAnim.Duration = animationSpeed;
			bdsAnim.TimingFunction = tf;

			// Image layer
			CALayer layer = new CALayer();
			layer.Contents = image.CGImage;
			layer.Position = center;
			layer.ZPosition = random.Next(-100, 99);
			layer.Actions = NSDictionary.FromObjectsAndKeys(new NSObject[]{posAnim, bdsAnim}, new NSObject[]{new NSString("position"), new NSString("bounds")});

			// Animation for text layer
			CATransform3D scale = CATransform3D.MakeScale(0.0f, 0.0f, 0.0f);
			CABasicAnimation tScaleAnim = CABasicAnimation.FromKeyPath("transform");
			tScaleAnim.From = NSValue.FromCATransform3D(scale);
			tScaleAnim.Duration = animationSpeed;
			tScaleAnim.TimingFunction = tf;

			// text layer
			CATextLayer fileNameLayer = new CATextLayer();
			fileNameLayer.FontSize = 24;
			fileNameLayer.ForegroundColor = NSColor.White.CGColor;
			SetText(" " + filename + " ", fileNameLayer);
			fileNameLayer.Transform = scale;
			fileNameLayer.Position = CGPoint.Empty;
			fileNameLayer.AnchorPoint = CGPoint.Empty;
			fileNameLayer.ShadowColor = NSColor.Black.CGColor;
			fileNameLayer.ShadowOffset = new CGSize(5, 5);
			fileNameLayer.ShadowOpacity = 1.0f;
			fileNameLayer.ShadowRadius = 0.0f;
			fileNameLayer.BorderColor = NSColor.White.CGColor;
			fileNameLayer.BorderWidth = 1.0f;
			fileNameLayer.Actions = NSDictionary.FromObjectsAndKeys(new NSObject[]{tScaleAnim}, new NSObject[]{new NSString("transform")});

			layer.AddSublayer(fileNameLayer);
			View.Layer.AddSublayer(layer);

			CATransaction.Begin();
			layer.Position = randomPoint;
			layer.Bounds = imageBounds;
			fileNameLayer.Transform = CATransform3D.Identity;
			CATransaction.Commit();
		}

		//- (void)setText:(NSString *)text;
		void SetText(string text, CATextLayer tl)
		{
			NSFont font = NSFont.SystemFontOfSize(tl.FontSize);
			NSDictionary attrs = NSDictionary.FromObjectsAndKeys(new NSObject[]{font}, new NSObject[]{NSStringAttributeKey.Font});
			CGSize size = text.StringSize(attrs);
			// Ensure that the size is in whole numbers:
			size.Width = (nfloat)Math.Ceiling(size.Width);
			size.Height = (nfloat)Math.Ceiling(size.Height);
			CGRect bounds = new CGRect(0, 0, size.Width, size.Height);
			tl.Bounds = bounds;
			if (tl.SuperLayer == textContainer ) {
				tl.SuperLayer.Bounds = new CGRect(0, 0, size.Width + 16, size.Height + 20);
			}

			tl.String = text;
		}

		async Task<NSData> LoadImageDataFromFileAsync(string filepath)
		{
			NSData imageData = NSData.FromFile(filepath);
			return imageData;
		}
		#endregion    
	}
}
