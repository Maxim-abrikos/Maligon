using Assimp;
using HelixToolkit.Wpf;
using Maligon.SubClasses;
using Maligon.WorkClasses;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Maligon
{

    public partial class MainWindow : Window
    {
        private Checker checker;
        private bool IsLoaded = false;
        private ModelLoader modelLoader;
        private HelixMeshPresenter _presenter;
        private LodMesh _selectedLod;
        private ModelImportResult _result;
        public MainWindow(Checker checker, ModelLoader loader)
        { 
            InitializeComponent();
            this.checker = checker;
            this._presenter = new HelixMeshPresenter(MainViewport);
            this.modelLoader = loader;
            MainGridLabel.Content = "Загрузите файл формата .obj, .gltf, .FBX";
        }

        private void EnterDDZone(object sender, DragEventArgs e)
        {
            if (IsLoaded == false)
            {
                (bool, string) Value = this.checker.CheckFormat((string[])e.Data.GetData(DataFormats.FileDrop));
                if (Value.Item1)
                    MainGridLabel.Content = "Формат поддерживается";
                else
                    MainGridLabel.Content = Value.Item2;
            }
        }

        private void DropDDZone(object sender, DragEventArgs e)
        {
            if (IsLoaded == false)
            {
                (bool, string) Value = this.checker.CheckFormat((string[])e.Data.GetData(DataFormats.FileDrop));
                if (Value.Item1)
                {
                    MainGridLabel.Content = "";
                    IsLoaded = true;
                    _result = modelLoader.Load(Value.Item2);
                    Debug.WriteLine($"LOD count: {_result.LodModel?.Lods?.Count}");
                    LodItemsControl.ItemsSource = _result.LodModel.Lods;
                    _presenter.Show(_result.LodModel.Lods[0].Mesh);
                    _selectedLod = _result.LodModel.Lods[0];

                }
                else
                    MainGridLabel.Content = Value.Item2;
            }
        }

        private void LodButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LodMesh lod)
            {
                _selectedLod = lod;

                if (lod.Mesh != null)
                {
                    _presenter.Show(lod.Mesh);
                }
                else
                {
                    MessageBox.Show("MeshData отсутствует у выбранного LOD");
                }
            }
        }

        private void GenerateLod_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedLod == null || _selectedLod.Mesh == null)
            {
                MessageBox.Show("Модель не загружена");
                return;
            }

            // 1. Конвертация в MeshGraph
            var mesh = MeshConverter.ToMeshGraph(_selectedLod.Mesh);

            var occupancy = new OccupancyMap();


            //Вот тут может быть ссанина

            var analyzer = new MeshStructureAnalyzer(mesh, occupancy);
            var collapser = new LineCollapser(mesh);

            // 1. Анализ сетки
            analyzer.Analyze();
            var line = analyzer.BuildLine();

            if (line != null && line.Faces.Count > 1)
            {
                foreach (Maligon.SubClasses.Face silly in line.Faces)
                {
                    Debug.WriteLine(silly.V0 + " " + silly.V1 + " " + silly.V2);
                }
                collapser.Collapse(line);
                MessageBox.Show(line.Faces.Count().ToString());
            }
            

            // 6. Обратная конвертация
            var newMeshData = MeshConverter.ToMeshData(mesh);

            // 7. Обновление модели
            var newLod = new LodMesh
            {
                Mesh = newMeshData,
                // если есть Level / Name — скопируй/увеличь
            };

            _result.LodModel.Lods.Add(newLod);

            // обновляем UI
            LodItemsControl.ItemsSource = null;
            LodItemsControl.ItemsSource = _result.LodModel.Lods;

            // делаем его активным
            _selectedLod = newLod;

            // отображаем
            _presenter.Show(newMeshData);

            //MessageBox.Show($"Схлопнуто полигонов: {line.Faces.Count}");
        }



        private void LeaveDDZone(object sender, DragEventArgs e)
        {
            if (IsLoaded)
                MainGridLabel.Content = "";
            else
                MainGridLabel.Content = "Загрузите файл формата .obj, .gltf, .FBX";
        }

        private void DoubleClicked(object sender, MouseButtonEventArgs e)
        {

            if (IsLoaded == false)
            {
                OpenFileDialog file = new OpenFileDialog();
                { 
                file.Title = "Выберите файл модели";
                }
                if (file.ShowDialog() == true)
                {
                    (bool, string) Value = this.checker.CheckFormat((string[])file.FileNames);
                    if (Value.Item1)
                    {
                        MainGridLabel.Content = "";
                        IsLoaded = true;
                        _result = modelLoader.Load(Value.Item2);
                        _presenter.Show(_result.LodModel.Lods[0].Mesh);
                    }
                    else
                        MainGridLabel.Content = Value.Item2;
                }
            }

        }

        private void SaveModel(object sender, RoutedEventArgs e)
        {

            var dialog = new OpenFolderDialog
            {
                Title = "Куда сохранить файлы?"
            };

            if (dialog.ShowDialog() == true)
            {
                modelLoader.Export(_result, dialog.FolderName);
            }
        }
    }
}