﻿using System;
using Android.App;
using Android.Widget;
using Android.Views;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Android.Graphics;
using Android.Util;
using Android.Preferences;
using Android.OS;
using Android.Content;
using System.Threading;
using Java.Interop;

namespace PhotoGallery
{
	public class PhotoGalleryFragment : VisibleFragment
    {
		public static readonly string TAG = "PhotoGalleryFragment";

		public static readonly string PHOTO_URL_EXTRA = "photoUrl";
		public static readonly string PHOTO_FILENAME_EXTRA = "photoFilename";

		private static Task idlePreloadTask;
		private static CancellationTokenSource idlePreloadTokenSource;

		#region = member variables
		public GridView mGridView;
		List<GalleryItem> galleryItems;
		int currentPage;
		bool endReached;
		string query;
		string lastQuery;
		#endregion

		#region - Lifecycle methods
		public override async void OnCreate(Android.OS.Bundle savedInstanceState)
		{
//			Console.WriteLine("[{0}] OnCreate Called: {1}", TAG, DateTime.Now.ToLongTimeString());
			base.OnCreate(savedInstanceState);
			RetainInstance = true;
			SetHasOptionsMenu(true);

			currentPage = 1;
			endReached = false;

			await UpdateItems();

			// Start PollService - check for new pics in background
//			Intent i = new Intent(Activity, typeof(PollService));
//			Activity.StartService(i);
			// Same as above on a timer
//			PollService.SetServiceAlarm(Activity, true);

//			Console.WriteLine("[{0}] OnCreate Done: {1}", TAG, DateTime.Now.ToLongTimeString());
		}

		public override Android.Views.View OnCreateView(Android.Views.LayoutInflater inflater, Android.Views.ViewGroup container, Android.OS.Bundle savedInstanceState)
		{
//			Console.WriteLine("[{0}] OnCreateView Called: {1}", TAG, DateTime.Now.ToLongTimeString());
			View v = inflater.Inflate(Resource.Layout.fragment_photo_gallery, container, false);

			mGridView = v.FindViewById<GridView>(Resource.Id.gridView);

			mGridView.Scroll += async (object sender, AbsListView.ScrollEventArgs e) => {
				if (e.FirstVisibleItem + e.VisibleItemCount == e.TotalItemCount && !endReached && e.TotalItemCount > 0) {
					endReached = true;
//					Console.WriteLine("[{0}] Scroll Ended", TAG);
					currentPage++;

					List<GalleryItem> newItems;
					if (query != null) {
						newItems = await new FlickrFetchr().Search(query, currentPage.ToString());
					}
					else {
						newItems = await new FlickrFetchr().Fetchitems(currentPage.ToString());
					}

					galleryItems = galleryItems.Concat(newItems).ToList();

					var adapter = (ArrayAdapter)mGridView.Adapter;
					adapter.AddAll(newItems);
					//adapter.NotifyDataSetChanged();

					endReached = false;
				}
			};

			mGridView.ScrollStateChanged += (object sender, AbsListView.ScrollStateChangedEventArgs e) => {
				if (e.ScrollState == ScrollState.Idle) {
					if (idlePreloadTokenSource != null) {
						idlePreloadTokenSource.Cancel();
//						Console.WriteLine("[{0}] IdlePreloadTaskState: {1}", TAG, idlePreloadTask.IsCompleted);
					}
					idlePreloadTokenSource = new CancellationTokenSource();
					idlePreloadTask = Task.Run(async () => {
						await new FlickrFetchr().PreloadImages(mGridView.FirstVisiblePosition, mGridView.LastVisiblePosition, galleryItems).ConfigureAwait(false);
					}, idlePreloadTokenSource.Token);
				}
			};

			mGridView.ItemClick += (object sender, AdapterView.ItemClickEventArgs e) => {
				GalleryItem item = galleryItems[e.Position];
				var photoPageUri = Android.Net.Uri.Parse(item.PhotoPageUrl);

				// Implicit intent
//				Intent i = new Intent(Intent.ActionView, photoPageUri);
				// Explicit intent
				Intent i = new Intent(Activity, typeof(PhotoPageActivity));
				i.SetData(photoPageUri);

				StartActivity(i);
			};

			mGridView.ItemLongClick += (object sender, AdapterView.ItemLongClickEventArgs e) => {
				if (galleryItems[e.Position].Url != null && galleryItems[e.Position].Url != String.Empty) {
					Intent intent = new Intent(Activity, typeof(PhotoActivity));
					intent.PutExtra(PHOTO_URL_EXTRA, galleryItems[e.Position].Url);
					Activity.StartActivity(intent);
				}
			};

			SetupAdapter();
//			Console.WriteLine("[{0}] OnCreateView Done: {1}", TAG, DateTime.Now.ToLongTimeString());
			return v;
		}

		public override void OnCreateOptionsMenu(IMenu menu, MenuInflater inflater)
		{
			base.OnCreateOptionsMenu(menu, inflater);
			inflater.Inflate(Resource.Menu.fragment_photo_gallery, menu);
			// Using SearchView 
			if (Build.VERSION.SdkInt >= BuildVersionCodes.Honeycomb) {
				IMenuItem searchItem = menu.FindItem(Resource.Id.menu_item_search);
				SearchView searchView = (SearchView)searchItem.ActionView;

				// Get the data from our searchable.xml as a SearchableInfo
				SearchManager searchManager = (SearchManager)Activity.GetSystemService(Context.SearchService);
				ComponentName name = Activity.ComponentName;
				SearchableInfo searchInfo = searchManager.GetSearchableInfo(name);
				searchView.SetSearchableInfo(searchInfo);
				searchView.SubmitButtonEnabled = true;

				searchView.QueryTextFocusChange += (object sender, View.FocusChangeEventArgs e) => {
					if (e.HasFocus && lastQuery != null && lastQuery != String.Empty) {
						searchView.SetQuery(lastQuery, false);
						currentPage = 1;
						int id = searchView.Context.Resources.GetIdentifier("android:id/search_src_text", null, null);
						EditText editText = searchView.FindViewById<EditText>(id);
						editText.SelectAll();
					}
				};
				searchView.Close += async (object sender, SearchView.CloseEventArgs e) => {
					e.Handled = false;
					PreferenceManager.GetDefaultSharedPreferences(Activity).Edit().PutString(FlickrFetchr.PREF_SEARCH_QUERY, null).Commit();
					query = null;
					currentPage = 1;
					await UpdateItems();
				};
			}
		}

		// SearchView does not call these methods.
		public override bool OnOptionsItemSelected(IMenuItem item)
		{
			switch (item.ItemId) {
				case Resource.Id.menu_item_search:
					currentPage = 1;
					Activity.StartSearch(lastQuery, true, null, false);
					return true;
				case Resource.Id.menu_item_clear:
					PreferenceManager.GetDefaultSharedPreferences(Activity).Edit().PutString(FlickrFetchr.PREF_SEARCH_QUERY, null).Commit();
					currentPage = 1;
					Task.Run(() => {
					}).ContinueWith(async(t) => {
						await UpdateItems();
					}, TaskScheduler.FromCurrentSynchronizationContext());
					return true;
				case Resource.Id.menu_item_toggle_polling:
					bool shouldStartAlarm = !PollService.IsServiceAlarmOn(Activity);
					PollService.SetServiceAlarm(Activity, shouldStartAlarm);
					if (Build.VERSION.SdkInt >= BuildVersionCodes.Honeycomb)
						Activity.InvalidateOptionsMenu();
					return true;
				default:
					return base.OnOptionsItemSelected(item);
			}
		} 

		public override void OnPrepareOptionsMenu(IMenu menu)
		{
			base.OnPrepareOptionsMenu(menu);

			IMenuItem toggleItem = menu.FindItem(Resource.Id.menu_item_toggle_polling);
			if (PollService.IsServiceAlarmOn(Activity)) {
				toggleItem.SetTitle(Resource.String.stop_polling);
			}
			else {
				toggleItem.SetTitle(Resource.String.start_polling);
			}
		}

		#endregion

		#region - Adapter
		public async Task UpdateItems() {
			if (this.Activity == null)
				return;
			ProgressDialog pg = new ProgressDialog(Activity);
			pg.SetMessage(Resources.GetString(Resource.String.loading_images_message));
			pg.SetTitle(Resources.GetString(Resource.String.loading_images_title));
			pg.SetCancelable(false);
			pg.Show();

			query = PreferenceManager.GetDefaultSharedPreferences(Activity).GetString(FlickrFetchr.PREF_SEARCH_QUERY, null);
			if (query != null && query != String.Empty)
				lastQuery = query;
			FlickrFetchr fetchr = new FlickrFetchr();
			if (query != null) {
				galleryItems = await fetchr.Search(query, currentPage.ToString());
			}
			else {
				galleryItems = await fetchr.Fetchitems(currentPage.ToString());
			}
//			foreach (GalleryItem item in galleryItems) {
//				Console.WriteLine("[{0}]\nPhoto Id: {1}\nCaption: {2}\nUrl: {3}", TAG, item.Id, item.Caption, item.Url);
//			}
			SetupAdapter();
			Toast.MakeText(Activity, 
				String.Format("{0} {1}: {2}", 
				(query != null ? query + " " + Resources.GetString(Resource.String.search) : Resources.GetString(Resource.String.recent_photos)), 
				Resources.GetString(Resource.String.results), 
				fetchr.NumberOfHits), 
				ToastLength.Long)
				.Show();

			pg.Dismiss();
		}

		void SetupAdapter() {
			if (Activity == null || mGridView == null) return;

			if (galleryItems != null) {
				var adapter = new GalleryItemAdapter(Activity, galleryItems);
				mGridView.Adapter = adapter;
				Task.Run(async () => {
					Display display = Activity.WindowManager.DefaultDisplay;
					DisplayMetrics outMetrics = new DisplayMetrics();
					display.GetMetrics(outMetrics);

					float density = Activity.Resources.DisplayMetrics.Density;
					var height = outMetrics.HeightPixels / density;
					var width = outMetrics.WidthPixels / density;

					int itemsPerRow = (int)Math.Round(width/120);
					int numRows = (int)Math.Round(height/120);
					int numItems = itemsPerRow * numRows;

					await new FlickrFetchr().PreloadImages(0, numItems, galleryItems).ConfigureAwait(false);
				});
			}
			else {
				mGridView.Adapter = null;
			}

		}

    }

	public class GalleryItemAdapter : ArrayAdapter<GalleryItem>
	{
		Activity context;

		public GalleryItemAdapter(Activity context, List<GalleryItem> items) : base(context, 0 , items)
		{
			this.context = context;
		}

		public override View GetView(int position, View convertView, ViewGroup parent)
		{
			// Don't recycle
//			ImageView view = (ImageView)context.LayoutInflater.Inflate(Resource.Layout.gallery_item, parent, false);

			// Recycle
			CancellationTokenSource cts;
			ImageView view = (ImageView)convertView;
			if (view == null) {
				view = (ImageView)context.LayoutInflater.Inflate(Resource.Layout.gallery_item, parent, false);
			}
			else {
				var wrapper = view.Tag.JavaCast<Wrapper<CancellationTokenSource>>();
				cts = wrapper.Data;
				if (!cts.IsCancellationRequested) {
					cts.Cancel();
					Console.WriteLine("[{0}] Cancelled Image Load Requested: {1}", PhotoGalleryFragment.TAG, view.Handle);
				}
			}

			view.SetImageResource(Resource.Drawable.face_icon);
			cts = new CancellationTokenSource();
			view.Tag = new Wrapper<CancellationTokenSource> { Data = cts };
			var ct = cts.Token;

			GalleryItem item = GetItem(position);
			Task.Run(async () => {
				try {
					await LoadImage(view, item.Url, position, ct, item.Filename);
				}
				catch (System.OperationCanceledException ex)
				{
					Console.WriteLine("[{0}]*************** Image download cancelled: {1}, {2}", PhotoGalleryFragment.TAG, ex.Message, view.Handle);
				}
				catch (Exception ex)
				{
					Console.WriteLine("[{0}] Image download failed: {1}", PhotoGalleryFragment.TAG, ex.Message);
				}
			}, ct);


			return view;
		}

		public async Task LoadImage(ImageView imageView, string url, int position, CancellationToken token, string filename) {
//			Console.WriteLine("[{0}] Image Load started: {1}", PhotoGalleryFragment.TAG, imageView.Handle);
			token.ThrowIfCancellationRequested();
			Bitmap image = await new FlickrFetchr().GetImageBitmapAsync(url, position, token, filename);
			token.ThrowIfCancellationRequested();
			if (image != null) {
				context.RunOnUiThread(() => {
					imageView.SetImageBitmap(image);
				});
//				Console.WriteLine("[{0}] Image Load Finished: {1}", PhotoGalleryFragment.TAG, imageView.Handle);
			}
			else {
				Console.WriteLine("[{0}] Image null: {1}", PhotoGalleryFragment.TAG, imageView.Handle);
			}
		}
	}
	#endregion

	public class Wrapper<T>: Java.Lang.Object
	{
		public T Data;
	}
}

