﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Navigation;
using Grabacr07.KanColleViewer.Models;
using MetroRadiance.Core;
using mshtml;
using SHDocVw;
using IServiceProvider = Grabacr07.KanColleViewer.Win32.IServiceProvider;
using WebBrowser = System.Windows.Controls.WebBrowser;
using KCVSettings = Grabacr07.KanColleViewer.Models.Settings;

namespace Grabacr07.KanColleViewer.Views.Controls
{
	[ContentProperty("WebBrowser")]
	[TemplatePart(Name = PART_ContentHost, Type = typeof(ScrollViewer))]
	public class KanColleHost : Control
	{
		private const string PART_ContentHost = "PART_ContentHost";
		private static readonly Size kanColleSize = new Size(800.0, 480.0);
		private static readonly Size browserSize = new Size(800.0, 480.0);

		static KanColleHost()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(KanColleHost), new FrameworkPropertyMetadata(typeof(KanColleHost)));
		}

		private ScrollViewer scrollViewer;
		private bool styleSheetApplied;
		private Dpi? systemDpi;

		#region WebBrowser 依存関係プロパティ

		public WebBrowser WebBrowser
		{
			get { return (WebBrowser)this.GetValue(WebBrowserProperty); }
			set { this.SetValue(WebBrowserProperty, value); }
		}

		public static readonly DependencyProperty WebBrowserProperty =
			DependencyProperty.Register("WebBrowser", typeof(WebBrowser), typeof(KanColleHost), new UIPropertyMetadata(null, WebBrowserPropertyChangedCallback));

		private static void WebBrowserPropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var instance = (KanColleHost)d;
			var newBrowser = (WebBrowser)e.NewValue;
			var oldBrowser = (WebBrowser)e.OldValue;

			if (oldBrowser != null)
			{
				oldBrowser.LoadCompleted -= instance.HandleLoadCompleted;
				oldBrowser.LoadCompleted -= instance.ApplyFlashQualityScript;
			}
			if (newBrowser != null)
			{
				newBrowser.LoadCompleted += instance.HandleLoadCompleted;
				newBrowser.LoadCompleted += instance.ApplyFlashQualityScript;
			}
			if (instance.scrollViewer != null)
			{
				instance.scrollViewer.Content = newBrowser;
			}

			WebBrowserHelper.SetAllowWebBrowserDrop(newBrowser, false);
		}

		#endregion

		#region ZoomFactor 依存関係プロパティ

		/// <summary>
		/// ブラウザーのズーム倍率を取得または設定します。
		/// </summary>
		public double ZoomFactor
		{
			get { return (double)this.GetValue(ZoomFactorProperty); }
			set { this.SetValue(ZoomFactorProperty, value); }
		}

		/// <summary>
		/// <see cref="ZoomFactor"/> 依存関係プロパティを識別します。
		/// </summary>
		public static readonly DependencyProperty ZoomFactorProperty =
			DependencyProperty.Register("ZoomFactor", typeof(double), typeof(KanColleHost), new UIPropertyMetadata(1.0, ZoomFactorChangedCallback));

		private static void ZoomFactorChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var instance = (KanColleHost)d;

			instance.Update();
		}

		#endregion


		public KanColleHost()
		{
			this.Loaded += (sender, args) => this.Update();
		}

		public override void OnApplyTemplate()
		{
			base.OnApplyTemplate();

			this.scrollViewer = this.GetTemplateChild(PART_ContentHost) as ScrollViewer;
			if (this.scrollViewer != null)
			{
				this.scrollViewer.Content = this.WebBrowser;
			}
		}


		public void Update()
		{
			if (this.WebBrowser == null) return;

			var dpi = this.systemDpi ?? (this.systemDpi = this.GetSystemDpi()) ?? Dpi.Default;
			var zoomFactor = dpi.ScaleX + (this.ZoomFactor - 1.0);
			var percentage = (int)(zoomFactor * 100);

			ApplyZoomFactor(this.WebBrowser, percentage);

			if (this.styleSheetApplied)
			{
				this.WebBrowser.Width = (kanColleSize.Width * (zoomFactor / dpi.ScaleX)) / dpi.ScaleX;
				this.WebBrowser.Height = (kanColleSize.Height * (zoomFactor / dpi.ScaleY)) / dpi.ScaleY;
				this.MinWidth = this.WebBrowser.Width;
			}
			else
			{
				this.WebBrowser.Width = double.NaN;
				this.WebBrowser.Height = (browserSize.Height * (zoomFactor / dpi.ScaleY)) / dpi.ScaleY;
				this.MinWidth = (browserSize.Width * (zoomFactor / dpi.ScaleX)) / dpi.ScaleX;
			}
		}

		private static void ApplyZoomFactor(WebBrowser target, int zoomFactor)
		{
			if (zoomFactor < 10 || zoomFactor > 1000)
			{
				StatusService.Current.Notify(string.Format(Properties.Resources.ZoomAction_OutOfRange, zoomFactor));
				return;
			}

			try
			{
				var provider = target.Document as IServiceProvider;
				if (provider == null) return;

				object ppvObject;
				provider.QueryService(typeof(IWebBrowserApp).GUID, typeof(IWebBrowser2).GUID, out ppvObject);
				var webBrowser = ppvObject as IWebBrowser2;
				if (webBrowser == null) return;

				object pvaIn = zoomFactor;
				webBrowser.ExecWB(OLECMDID.OLECMDID_OPTICAL_ZOOM, OLECMDEXECOPT.OLECMDEXECOPT_DODEFAULT, ref pvaIn);
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
				StatusService.Current.Notify(string.Format(Properties.Resources.ZoomAction_ZoomFailed, ex.Message));
			}
		}

		private void HandleLoadCompleted(object sender, NavigationEventArgs e)
		{
			this.ApplyStyleSheet();
			WebBrowserHelper.SetScriptErrorsSuppressed(this.WebBrowser, true);

			this.Update();

            //var window = Window.GetWindow(this.WebBrowser);
            //if (window != null)
            //{
            //    window.Width = this.WebBrowser.Width;
            //}
		}

		private void ApplyStyleSheet()
		{
			try
			{
				var document = this.WebBrowser.Document as HTMLDocument;
				if (document == null) return;

				var gameFrame = document.getElementById("game_frame");
				if (gameFrame == null)
				{
					if (document.getElementById("flashWrap") != null)
					{
						gameFrame = document.documentElement;
					}
					else if (document.url.Contains(".swf?"))
					{
						gameFrame = document.body;
					}
				}

				if (gameFrame != null)
				{
					var target = gameFrame.document as HTMLDocument;
					if (target != null)
					{
						target.createStyleSheet().cssText = Properties.Settings.Default.OverrideStyleSheet;
						this.styleSheetApplied = true;
						return;
					}
				}
			}
			catch (Exception ex)
			{
				StatusService.Current.Notify("failed to apply css: " + ex.Message);
			}

			return;
		}

		public void ApplyFlashQualityScript(object sender, NavigationEventArgs e)
		{
			try
			{
				var document = this.WebBrowser.Document as HTMLDocument;
				FramesCollection frames = document.frames;
				HTMLDocument mainFrame = null;
				for (int i = 0; i < frames.length; i++)
				{
					object refIndex = i;
					IHTMLDocument2 frame = CrossFrameIE.GetDocumentFromWindow((IHTMLWindow2)frames.item(ref refIndex));
					if (frame != null && ((HTMLDocument)frame).getElementById("flashWrap") != null)
						mainFrame = (HTMLDocument)frame;
					else
						mainFrame = document;
				}

				if (mainFrame != null)
				{
					// Javascript injection - Greasemonkey style. Suppose to be more dynamic on DOM objects.
					// Main reason for JS method is that the flash itself doesn't exist until after it has been added to the "flashWrap" DIV element!
					// Leave the timing of when the flash is added to the script.
					IHTMLElement head = (IHTMLElement)((IHTMLElementCollection)mainFrame.all.tags("head")).item(null, 0);
					IHTMLScriptElement scriptOjbect = (IHTMLScriptElement)mainFrame.createElement("script");
					scriptOjbect.type = @"text/javascript";
					scriptOjbect.text = string.Format(Properties.Settings.Default.FlashQualityJS, KCVSettings.Current.FlashQuality, KCVSettings.Current.FlashWindow);
					((HTMLHeadElement)head).appendChild((IHTMLDOMNode)scriptOjbect);
				}

				if (mainFrame == null && document.url.Contains(".swf?"))
				{
					// No dynamic way of accessing and editing this, so we forcefully make our own embed since its already provided for us.
					document.body.innerHTML = string.Format(Properties.Settings.Default.FlashEmbed, KCVSettings.Current.FlashQuality, KCVSettings.Current.FlashWindow, document.url);
				}
			}
			catch (Exception ex)
			{
				StatusService.Current.Notify("Failed to apply quality setting: " + ex.Message);
			}
		}
	}
}
