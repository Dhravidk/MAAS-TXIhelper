﻿using System;
using itk.simple;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using V = VMS.TPS.Common.Model.API;
using SimpleProgressWindow;
using System.Windows.Forms;
using System.Threading;
using System.IO;
using System.Linq;
using MAAS_TXIHelper.Models;
using I = itk.simple;

namespace MAAS_TXIHelper.Core
{
    public class CTConcat : SimpleMTbase
    {
        private Patient _patient;
        private ImageModel _imagePrimary;
        private ImageModel _imageSecondary;
        private Registration _registration;
        private string _saveDir;
        private double _spacingMM;

        public CTConcat(V.Patient patient, ImageModel imagePrimary, ImageModel imageSecondary, V.Registration registration, string saveDir, double spacingMM) {
            _patient = patient;
            _imagePrimary = imagePrimary;
            _imageSecondary = imageSecondary;
            _registration = registration;
            _saveDir = saveDir;
            _spacingMM = spacingMM; 
        }

        
        
        public override bool Run()
        {

            ProvideUIUpdate("Starting CT Concat");
            var itkImagePrimary = _imagePrimary.BuildITKImage(_spacingMM);
            var itkImageSecondary = _imageSecondary.BuildITKImage(_spacingMM);
            

            for (int z = 0; z < _imagePrimary.ZSize; z++)
            {
                double progDec = (double) z / _imagePrimary.ZSize;
                int progInt = (int)(progDec * 50);
                var point = itkImagePrimary.TransformIndexToPhysicalPoint(new VectorInt64(new Int64[] { 0, 0, z }));
                var msg = $"\rReading primary image plane at index: {z}\tZ coordinate: {point[2]}";

                if (z % 10 == 0)
                {
                    // Slice is a multiple of 10, show message update
                    ProvideUIUpdate(progInt, msg);
                }
                else
                {
                    ProvideUIUpdate(progInt);
                }
                UpdateUILabel(msg);
                //ProvideUIUpdate(progInt, message: $"\rReading primary image plane at index: {z}/{nPlanes}");
                
                //ProvideUIUpdate($"\rReading primary image plane at index: {z}/{nPlanes}");
                _imagePrimary.VImage.GetVoxels(z, _imagePrimary.VoxelPlane);
                for (int x = 0; x < _imagePrimary.VImage.XSize; x++)
                {
                    for (int y = 0; y < _imagePrimary.VImage.YSize; y++)
                    {
                        _imagePrimary.VoxelVolume[x, y, z] = _imagePrimary.VoxelPlane[x, y];
                        _imagePrimary.HuValues[x, y, z] = _imagePrimary.VImage.VoxelToDisplayValue(_imagePrimary.VoxelPlane[x, y]);
                        itkImagePrimary.SetPixelAsFloat(
                            new VectorUInt32(new uint[] { (uint)x, (uint)y, (uint)z }), (float)(_imagePrimary.HuValues[x, y, z]));
                    }
                }
            }
            ProvideUIUpdate($"\nData processing for primary image complete.");


            for (int z = 0; z < _imageSecondary.ZSize; z++)
            {
                double progDec = (double)z / _imageSecondary.ZSize;
                int progInt = 50 + (int)(progDec * 50);
                var point = itkImagePrimary.TransformIndexToPhysicalPoint(new VectorInt64(new Int64[] { 0, 0, z }));
                var msg = $"\rReading secondary image plane at index: {z}\tZ coordinate: {point[2]}";

                if(z % 10 == 0)
                {
                    // Slice is a multiple of 10, show message update
                    ProvideUIUpdate(progInt, msg);
                }
                else
                {
                    ProvideUIUpdate(progInt);
                }
                UpdateUILabel(msg);
                

                _imageSecondary.VImage.GetVoxels(z, _imageSecondary.VoxelPlane);
                for (int x = 0; x < _imageSecondary.VImage.XSize; x++)
                {
                    for (int y = 0; y < _imageSecondary.VImage.YSize; y++)
                    {
                        _imageSecondary.VoxelVolume[x, y, z] = _imageSecondary.VoxelPlane[x, y];
                        _imageSecondary.HuValues[x, y, z] = _imageSecondary.VImage.VoxelToDisplayValue(_imageSecondary.VoxelPlane[x, y]);
                        itkImageSecondary.SetPixelAsFloat(
                            new VectorUInt32(
                                new uint[] { (uint)x, (uint)y, (uint)z }), (float)(_imageSecondary.HuValues[x, y, z]));
                    }
                }
            }

            ProvideUIUpdate($"\nData processing for primary image/secondary image complete.");
            ProvideUIUpdate("loaded secondary, starting load registration");
            itk.simple.Image itkImageSecondaryTransformed = TransformImage(itkImageSecondary, _registration);
            ProvideUIUpdate("loaded registration");
            ProvideUIUpdate("About to merge images. This step can take several minutes, patience please.");
            itk.simple.Image itkImageMerged = MergeImages(itkImagePrimary, itkImageSecondaryTransformed);
            SaveImagesDICOM(itkImageMerged, _imagePrimary.VImage);
            ProvideUIUpdate(100, "Complete");
            return true;
        }

        private bool PixelIndexOutofBound(VectorInt64 indexPrimary, itk.simple.Image itkImageSecondaryTransformed)
        {
            int x = (int)indexPrimary[0];
            int y = (int)indexPrimary[1];
            int z = (int)indexPrimary[2];
            if (x < 0 || x >= itkImageSecondaryTransformed.GetSize()[0])
                return true;
            if (y < 0 || y >= itkImageSecondaryTransformed.GetSize()[1])
                return true;
            if (z < 0 || z >= itkImageSecondaryTransformed.GetSize()[2])
                return true;
            return false;
        }
        
        private void SaveImagesDICOM(itk.simple.Image itkImageMerged, VMS.TPS.Common.Model.API.Image imagePrimary)
        {

            PixelIDValueEnum pixelType = PixelIDValueEnum.sitkFloat32;
            PixelIDValueEnum pixelTypeDCM = PixelIDValueEnum.sitkInt16;
            VectorUInt32 imageSize = new VectorUInt32(new uint[] { (uint)imagePrimary.XSize, (uint)imagePrimary.YSize });
            itk.simple.Image itkImage = new itk.simple.Image(imageSize, pixelType);
            itk.simple.Image itkImageDCM = new itk.simple.Image(imageSize, pixelTypeDCM);
            VectorDouble spacing = new VectorDouble(new double[] { imagePrimary.XRes, imagePrimary.YRes * 2 });
            VectorDouble spacingDCM = new VectorDouble(new double[] { imagePrimary.XRes, imagePrimary.YRes });
            itkImage.SetSpacing(spacing);
            itkImageDCM.SetSpacing(spacingDCM);
            ImageFileWriter writer = new ImageFileWriter();
            writer.KeepOriginalImageUIDOn();
            // DICOM metadata that are common to each slice
            itkImageDCM.SetMetaData("0010|0010", "demo^pt");
            itkImageDCM.SetMetaData("0010|0010", "demo^pt");
            itkImageDCM.SetMetaData("0010|0020", "demopatient");
            itkImageDCM.SetMetaData("0008|0008", "ORIGINAL\\PRIMARY\\AXIAL");
            itkImageDCM.SetMetaData("0008|0070", imagePrimary.Series.ImagingDeviceManufacturer);
            itkImageDCM.SetMetaData("0008|0020", "20230519");  // study date  -- to be updated to be the app running date
            itkImageDCM.SetMetaData("0008|0030", "084002.187034"); // study time -- to be updated to be the app running time
            itkImageDCM.SetMetaData("0018|0050", imagePrimary.ZRes.ToString()); // slice thickness
                                                                                // itkImageDCM.SetMetaData("0020|0012", ?); // acquisition number
            itkImageDCM.SetMetaData("0020|000D", imagePrimary.Series.Study.UID.Substring(0, imagePrimary.Series.Study.UID.Length - 1));  // study UID.
            string newSeriesUID = imagePrimary.Series.UID.Substring(0, imagePrimary.Series.UID.Length - 1);
            itkImageDCM.SetMetaData("0020|000E", newSeriesUID);  // series UID.
            itkImageDCM.SetMetaData("0020|0052", imagePrimary.Series.FOR);  // use the same frame of reference UID as the original image series.
            itkImageDCM.SetMetaData("0020|1040", "BB"); // position reference indicator
            itkImageDCM.SetMetaData("0020|0012", "1"); // acquisition number
            for (int z = 0; z < itkImageMerged.GetSize()[2]; z++)
            {
                ProvideUIUpdate($"Processing Slice {z}");
                
                
                for (int x = 0; x < itkImageMerged.GetSize()[0]; x++)
                {
                    for (int y = 0; y < itkImageMerged.GetSize()[1]; y++)
                    {
                        //                        itkImage.SetPixelAsFloat(new VectorUInt32(new uint[] { (uint)x, (uint)y }), (float)(huValues[x, y, z] / 200.0));
                        itkImageDCM.SetPixelAsInt16(new VectorUInt32(new uint[] { (uint)x, (uint)y }), (Int16)itkImageMerged.GetPixelAsFloat(new VectorUInt32(new uint[] { (uint)x, (uint)y, (uint)z })));
                    }
                }
                if (imagePrimary.Series.Modality == SeriesModality.CT)
                {
                    itkImageDCM.SetMetaData("0008|0060", "CT");
                    itkImageDCM.SetMetaData("0018|0060", "120");
                }
                string imagePositionPatient = $"250\\250\\{z * imagePrimary.ZRes}";
                itkImageDCM.SetMetaData("0020|0032", imagePositionPatient);  // image position patient.
                itkImageDCM.SetMetaData("0020|1041", $"{z * imagePrimary.ZRes}");  // slice location
                Console.Write($"\rSaving DICOM file for slice index: {z}   ");
                
                writer.SetFileName(Path.Combine(_saveDir, $"{_patient.Id}_merged_{z}.DCM"));
                writer.Execute(itkImageDCM);
            }
            Console.WriteLine($"All DICOM files were saved.");

        }

        private itk.simple.Image MergeImages(itk.simple.Image itkImagePrimary, itk.simple.Image itkImageSecondaryTransformed)
        {
            // first define the merged image
            int newSlices = (int)((itkImagePrimary.GetOrigin()[2] - itkImageSecondaryTransformed.GetOrigin()[2]) / itkImagePrimary.GetSpacing()[2]) + (int)itkImagePrimary.GetSize()[2];
            MessageBox.Show($"Num new slices {newSlices}");
            PixelIDValueEnum pixelType = PixelIDValueEnum.sitkFloat32;
            VectorUInt32 image3DSize = new VectorUInt32(new uint[] { (uint)itkImagePrimary.GetSize()[0], (uint)itkImagePrimary.GetSize()[1], (uint)newSlices });
            itk.simple.Image itkImageMerged = new itk.simple.Image(image3DSize, pixelType);
            itkImageMerged.SetSpacing(itkImagePrimary.GetSpacing());
            double newOriginZ = itkImagePrimary.GetOrigin()[2] - (newSlices - itkImagePrimary.GetSize()[2]) * itkImagePrimary.GetSpacing()[2];
            itkImageMerged.SetOrigin(new VectorDouble(new double[] { itkImagePrimary.GetOrigin()[0], itkImagePrimary.GetOrigin()[1], newOriginZ }));
            //Console.WriteLine($"Size for merged: {itkImageMerged.GetSize()[0]} {itkImageMerged.GetSize()[1]} {itkImageMerged.GetSize()[2]}");
            // here we first construct a SimpleITK 3D image from the image data
            // 1. Based on ESAPI manual, the DICOM origin is the DICOM coordinate for the point at the upper left corner of the first imaging plane.
            //    Note that this DICOM origin does not have [0, 0, 0] as coordinates.
            //    The [0, 0, 0] point in the DICOM coordinate is a different point that was set during imaging scan, usually denoted by the BBs.
            //    In Eclipse, the displayed coordinates are relative to the user origin.
            // 2. The user origin (image.UserOrigin) is the user origin offset from DICOM origin. You can find the coordinates in Eclipse by looking at the property
            //    of the User Origin in the External Beam Planning workspace.
            for (int z = 0; z < itkImageMerged.GetSize()[2]; z++)
            {
                Console.Write($"\rCreating merged image for slice #{z}   ");
                for (int x = 0; x < itkImageMerged.GetSize()[0]; x++)
                {
                    for (int y = 0; y < itkImageMerged.GetSize()[1]; y++)
                    {
                        VectorInt64 pixelIndex = new VectorInt64(new long[] { x, y, z });
                        var physicalCoordinate = itkImageMerged.TransformIndexToPhysicalPoint(pixelIndex);
                        if (physicalCoordinate[2] >= itkImagePrimary.GetOrigin()[2])
                        {
                            var indexPrimary = itkImagePrimary.TransformPhysicalPointToIndex(physicalCoordinate);
                            var iPrimary32 = new VectorUInt32(new uint[] { (uint)indexPrimary[0], (uint)indexPrimary[1], (uint)indexPrimary[2] });
                            var pixelValue = itkImagePrimary.GetPixelAsFloat(iPrimary32);
                            itkImageMerged.SetPixelAsFloat(new VectorUInt32(new uint[] { (uint)x, (uint)y, (uint)z }), pixelValue);
                        }
                        else
                        {
                            var index = itkImageSecondaryTransformed.TransformPhysicalPointToIndex(physicalCoordinate);
                            if (PixelIndexOutofBound(index, itkImageSecondaryTransformed))
                            {
                                itkImageMerged.SetPixelAsFloat(new VectorUInt32(new uint[] { (uint)x, (uint)y, (uint)z }), -1000);
                            }
                            else
                            {
                                var iPrimary32 = new VectorUInt32(new uint[] { (uint)index[0], (uint)index[1], (uint)index[2] });
                                var pixelValue = itkImageSecondaryTransformed.GetPixelAsFloat(iPrimary32);
                                itkImageMerged.SetPixelAsFloat(new VectorUInt32(new uint[] { (uint)x, (uint)y, (uint)z }), pixelValue);
                            }
                        }
                    }
                }
            }
            Console.WriteLine($"\nMerged image was created with {itkImageMerged.GetSize()[2]} slices.");
            return itkImageMerged;
        }

        private itk.simple.Image TransformImage(itk.simple.Image itkImageSecondary, Registration registration)
        {
            // Read image registration data. It is a 4 x 4 matrix.
            double[,] rMatrix = registration.TransformationMatrix;
            Console.WriteLine($"Registration matrix (rank: {rMatrix.Rank} length: {rMatrix.Length})");
            for (int i = 0; i < rMatrix.GetLength(0); i++)
            {
                for (int j = 0; j < rMatrix.GetLength(1); j++)
                {
                    Console.Write($"{rMatrix[i, j]}\t");
                }
                Console.WriteLine();
            }
            var tf = new TranslationTransform(3);
            tf.SetOffset(new VectorDouble(new double[] { rMatrix[0, 3], rMatrix[1, 3], rMatrix[2, 3] }));
            var transformed = SimpleITK.Resample(itkImageSecondary, tf);
            VectorDouble originOld = itkImageSecondary.GetOrigin();
            originOld[0] = originOld[0] + rMatrix[0, 3];
            originOld[1] = originOld[1] + rMatrix[1, 3];
            originOld[2] = originOld[2] + rMatrix[2, 3];
            itkImageSecondary.SetOrigin(originOld);
            return itkImageSecondary;
        }

        private itk.simple.Image ReadImage(VMS.TPS.Common.Model.API.Image imageEclipse)
        {
            int[,] voxelPlane = new int[imageEclipse.XSize, imageEclipse.YSize];
            int[,,] voxelVolume = new int[imageEclipse.XSize, imageEclipse.YSize, imageEclipse.ZSize];
            double[,,] huValues = new double[imageEclipse.XSize, imageEclipse.YSize, imageEclipse.ZSize];
            // here we first construct a SimpleITK 3D image from the image data
            // 1. Based on ESAPI manual, the DICOM origin is the DICOM coordinate for the point at the upper left corner of the first imaging plane.
            //    Note that this DICOM origin does not have [0, 0, 0] as coordinates.
            //    The [0, 0, 0] point in the DICOM coordinate is another point that was set during imaging scan.
            //    In Eclipse, the displayed coordinates are relative to the user origin.
            // 2. The user origin (image.UserOrigin) is the user origin offset from DICOM origin. You can find the coordinates in Eclipse by looking at the property
            //    of the User Origin in the External Beam Planning workspace.
            PixelIDValueEnum pixelType = PixelIDValueEnum.sitkFloat32;

            //VectorUInt32 mouthSize = new VectorUInt32(new uint[] { 64, 18 });

            itk.simple.VectorUInt32 image3DSize = new itk.simple.VectorUInt32(new uint[] { (uint)imageEclipse.XSize, (uint)imageEclipse.YSize, (uint)imageEclipse.ZSize });
            
            
            itk.simple.Image itkImage3D = new itk.simple.Image(image3DSize, pixelType);
          
            VectorDouble spacing3D = new VectorDouble(new double[] { imageEclipse.XRes, imageEclipse.YRes, imageEclipse.ZRes });
            itkImage3D.SetSpacing(spacing3D);
            VectorDouble origin = new VectorDouble(new double[] { imageEclipse.Origin.x, imageEclipse.Origin.y, imageEclipse.Origin.z });
            itkImage3D.SetOrigin(origin);
            int nPlanes = imageEclipse.ZSize;
            for (int z = 0; z < nPlanes; z++)
            {
                ProvideUIUpdate(((int)z/nPlanes));
                var point = itkImage3D.TransformIndexToPhysicalPoint(new VectorInt64(new Int64[] { 0, 0, z }));
                ProvideUIUpdate($"\rReading image plane at index: {z}\tZ coordinate: {point[2]}                   \t");
                
                
                imageEclipse.GetVoxels(z, voxelPlane);
                for (int x = 0; x < imageEclipse.XSize; x++)
                {
                    for (int y = 0; y < imageEclipse.YSize; y++)
                    {
                        voxelVolume[x, y, z] = voxelPlane[x, y];
                        huValues[x, y, z] = imageEclipse.VoxelToDisplayValue(voxelPlane[x, y]);
                        itkImage3D.SetPixelAsFloat(new VectorUInt32(new uint[] { (uint)x, (uint)y, (uint)z }), (float)(huValues[x, y, z]));
                    }
                }
            }
            ProvideUIUpdate($"\nData processing for image \"{imageEclipse.Id}\" complete.");
            return itkImage3D;
        }

        
    }
    
}