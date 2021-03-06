using Autofac;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

using Mallenom;
using Mallenom.Diagnostics.Logs;
using Mallenom.Imaging;
using Mallenom.Setup;
using Mallenom.Video;

using OpenCvSharp;
using OpenCvSharp.CPlusPlus;

using Recognizer.Database.Data;
using Recognizer.Detector;
using Recognizer.Recognition;
using Recognizer.Logs;

namespace Recognizer
{
	public partial class MainForm : Form
	{
		#region Data

		private IComponentContext _container;

		private IVideoSourceProvider _videoSourceProvider;
		private IVideoSource _videoSource;
		private IImageMatrix _matrix;

		private FaceDetector _detector;
		private LBPFaceRecognizer _recognizer;

		//private RecognitionLogController _recognitionLogController;

		private int _frameCounter;
		private int _skipFrames = 5;

		#endregion

		#region .ctor

		public MainForm(IComponentContext container)
		{
			Verify.Argument.IsNotNull(container, nameof(container));

			InitializeComponent();
			this.FormClosing += OnMainFormClosing;

			_container = container;

			Log = _container.Resolve<ILog>();

			InitializeRecognizer();
			InitializeVideoSource();
		}

		#endregion

		#region Properties

		private ILog Log { get; }

		#endregion

		#region Methods

		private void InitializeRecognizer()
		{
			_detector = _container.Resolve<FaceDetector>();
			_recognizer = _container.Resolve<LBPFaceRecognizer>();

			var trainDataPath = Path.Combine(
						Directory.GetCurrentDirectory(),
						"Samples",
						"LBPFaces.xml");

			_recognizer.Load(trainDataPath);

			_recognizer.FaceRecognized += OnFaceRecognized;
		}

		private void InitializeVideoSource()
		{
			_videoSourceProvider = _container.Resolve<IVideoSourceProvider>();
			_videoSource = _videoSourceProvider.CreateVideoSource();

			_matrix = new ColorMatrix();

			_frameImage.Matrix = _matrix;
			_frameImage.ManualUpdateRendererCache = true;
			_frameImage.SizeMode = ImageSizeMode.Zoom;

			StartVideoSource();
		}

		private void CloseVideoSource()
		{
			if (_videoSource != null)
			{
				_videoSource.DetachAllMatrices();
				_videoSource.MatrixUpdated -= OnMatrixUpdated;
				_videoSource.Close();
			}
		}

		private void StartVideoSource()
		{
			if (_videoSource == null || _videoSource.State == VideoSourceState.Running)
				return;

			_videoSource.AttachMatrix(_matrix);
			_videoSource.MatrixUpdated += OnMatrixUpdated;
			try
			{
				if (_videoSource.State == VideoSourceState.Closed)
					_videoSource.Open();
				_videoSource.Start();
			}
			catch (Exception exc)
			{
				Log.Error("Не удалось открыть видеоисточник.", exc);
			}
		}

		private void StopVideoSource()
		{
			if (_videoSource == null) return;

			_videoSource.DetachAllMatrices();
			_videoSource.MatrixUpdated -= OnMatrixUpdated;
			_videoSource.Stop();
		}

		private void DrawRectangles(Rect[] rectangles)
		{
			_frameImage.Frames.Clear();
			foreach(var rect in rectangles)
			{
				Rect rect_ = Shakalization(rect);

				_frameImage.Frames.Add(new Frame(rect_.X, rect_.Y, rect_.Width, rect_.Height)
				{
					Locked = true,
					Visible = true
				});
			}
			_frameImage.InvalidateCache();
		}

		private Rect Shakalization(Rect rect)
		{
			int frameWidth = _frameImage.Matrix.Width,
				frameHeigth = _frameImage.Matrix.Height,
				componentWidth = _frameImage.Width,
				componentHeigth = _frameImage.Height;

			double shakalizationFactor = (double)componentWidth / frameWidth;

			int shiftY = (int)(componentHeigth - (frameHeigth * shakalizationFactor)) / 2;

			rect.X = (int)(rect.X * shakalizationFactor);
			rect.Y = (int)((rect.Y * shakalizationFactor) + shiftY);

			rect.Width = (int)(rect.Width * shakalizationFactor);
			rect.Height = (int)(rect.Height * shakalizationFactor);

			return rect;
		}

		private void FaceProcessing()
		{
			foreach(var face in _detector.FacesRepository)
			{
				//double avgBrightness = face.AverageBrightness();

				//Mat mat = face.NormalizeBrightness(avgBrightness);

				//using(var w = new Window(face))
				//{
				//	Cv.WaitKey();
				//}
				//using(var w = new Window(mat))
				//{
				//	Cv.WaitKey();
				//}

				lock(this)
				{
					_recognizer.Recognize(face);

					//Log.Info(avgBrightness);
					//Log.Info(mat.AverageBrightness());
				}
			}

			_detector
					.FacesRepository
					.Clear();
		}
		#endregion

		#region EventHandlers
		
		/// <summary> Обработчик события <see cref="LBPFaceRecognizer.FaceRecognized"/>. </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void OnFaceRecognized(object sender, FaceRecognizedEventArgs e)
		{
			try
			{
				var employeeLogRepository = _container.Resolve<EmployeesLogRepository>();
				var filter = _container.Resolve<EmployeesLogRepositoryFilter>();
				filter.PersonLabel = e.Label;

				var employeeRecord = employeeLogRepository.FetchRecords(filter)[0];
				var resultStr = "Распознан " + employeeRecord + ". Уверенность: " + e.Confidence;

				Log.Info(resultStr);
			}
			catch(Exception exc)
			{
				Log.Error("Ошибка в запросе к БД", exc);
			}
		}

		private void OnMainFormClosing(object sender, FormClosingEventArgs e)
		{
			CloseVideoSource();

			_recognizer.FaceRecognized -= OnFaceRecognized;
		}

		private void OnMatrixUpdated(object sender, MatrixUpdatedEventArgs e)
		{
			var mi = new MethodInvoker(() =>
			{
				if(!IsDisposed)
				{
					_frameImage.InvalidateCache();
				}
			});

			if(_frameImage.InvokeRequired)
			{
				if(!IsDisposed)
				{
					try
					{
						_frameImage.BeginInvoke(mi);
					}
					catch(ObjectDisposedException)
					{
					}
				}
			}
			else
			{
				mi();
			}

			/**/
			if(_frameCounter >= _skipFrames)
			{
				//устанавливаем кадр, который требуется проанализировать
				_detector.UpdateImages(_matrix);

				var rects = _detector.DetectFaces();

				mi = new MethodInvoker(() => DrawRectangles(rects));
				if(_frameImage.InvokeRequired)
				{
					if(!IsDisposed)
					{
						try
						{
							_frameImage.BeginInvoke(mi);
						}
						catch(ObjectDisposedException)
						{
						}
					}
				}
				else
				{
					mi();
				}

				FaceProcessing();

				_frameCounter = 0;
			}
			_frameCounter++;
			/**/
		}

		private void OnRecognizeByPhotoToolStripMenuItem_Click(object sender, EventArgs e)
		{
			StopVideoSource();

			using (var openFileDlg = new OpenFileDialog())
			{ 
				openFileDlg.Title = "Выберите картинку";
				openFileDlg.InitialDirectory = Path.Combine(
					Directory.GetCurrentDirectory(),
					"Photo");
				openFileDlg.Filter = "Images|*.jpg;*.png;*.bmp|All files|*.*";

				if (openFileDlg.ShowDialog(this) == DialogResult.OK)
				{
					/**/
					var detector = new FaceDetector(openFileDlg.FileName);

					detector.DetectFaces();

					var trainDataPath = Path.Combine(
						Directory.GetCurrentDirectory(),
						"Samples",
						"LBPFaces.xml");

					foreach (var face in detector.FacesRepository)
					{
						int result = _recognizer.Recognize(face);
						string resultStr = string.Empty;

						if (result != -1)
						{
							try
							{
								var employeeLogRepository = _container.Resolve<EmployeesLogRepository>();
								var filter = _container.Resolve<EmployeesLogRepositoryFilter>();
								filter.PersonLabel = result;

								var employeeRecord = employeeLogRepository.FetchRecords(filter)[0];

								resultStr = "Распознан " + employeeRecord;

								Log.Info(resultStr);
							}
							catch (Exception exc)
							{
								Log.Error("Ошибка в запросе к БД", exc);
							}
						}

						else
						{
							resultStr = "Лицо не распознано";

							Log.Info(resultStr);
						}

						MessageBox.Show(resultStr, "Recognizer", MessageBoxButtons.OK);
					}

					detector
						.FacesRepository
						.Clear();
				}

				StartVideoSource();
			}
		}

		private void OnExitToolStripMenuItem_Click(object sender, EventArgs e)
		{
			this.Close();
		}

		private void OnCameraSettingsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using(var setupForm = new SingleControlSetupForm(_videoSourceProvider.CreateSetupControl(_videoSource)))
			{
				if(setupForm.ShowDialog(this) == DialogResult.OK)
				{
					StopVideoSource();
					StartVideoSource();
				}
			}
		}

		private void OnListsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			//_videoSource.Stop();
			//_videoSource.Close();

			//var state = _videoSource.State;

			//if(_videoSource != null)
			//{
			//	_videoSource.DetachAllMatrices();
			//	_videoSource.MatrixUpdated -= OnMatrixUpdated;
			//}

			CloseVideoSource();

			_recognizer.FaceRecognized -= OnFaceRecognized;

			using(var formLists = new UserLists(_container, _videoSource))
			{
				formLists.ShowDialog(this);
			}

			_recognizer.FaceRecognized += OnFaceRecognized;

			StartVideoSource();
			//_videoSource.MatrixUpdated += OnMatrixUpdated;

			//_videoSource.AttachMatrix(_matrix);

			//_videoSource.Open();
			//_videoSource.Start();
		}

		#endregion
	}
}