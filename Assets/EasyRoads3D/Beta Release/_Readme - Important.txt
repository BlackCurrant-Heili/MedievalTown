v3.3.b21 : 23-12-2025

IMPORTANT: Please backup your project first before importing the v3.3 beta in projects with existing EasyRoads3D road networks.

It is recommended to import this v3.3 beta package in a new project and first explore the new v3.3 features and options.


Visit https://www.easyroads3d.com/v3beta.php for beta version release notes, general v3.3 information and known issues.


# Install Notes

The "EasyRoads3D.v3.3b21" package includes all the v3.3 beta assets including the v3.3 beta demo scene for Unity 6

For Unity 2022 first import the main EasyRoads3D.v3.3b21 package and after that import the Unity 2022 update

When the full v3.3 beta package was already imported previously and beta demo scene specific assets are not required, only the Unity version specific update package can be imported

Package: "EasyRoads3D.v3.3b21.UI - Update" - when only the Unity 2022 or Unity 6 update package is imported the "EasyRoads3D.v3.3b21.UI - Update" package is also required, this package includes new UI related assets


# URP & HDRP Notes - Pink Materials

When the full Beta package is imported URP / HDRP shaders will be overwritten, to prevent this the folder /EasyRoads3D/Shaders/ can be deselected when importing the beta package. Otherwsie the required version specific URP / HDRP package can be reimported from /Assets/SRP Support Packages/ 

The Multi-Lane URP and HDRP packages include render pipeline specific shaders of the new multi-lane shader in v3.3, these packages should be imported manually when required

Remaining pink materials in the beta Demo scene can be fixed by running the Unity Render Pipeline Conversion tool

Website: http://www.easyroads3d.com
Support: info@easyroads3d.com

https://discussions.unity.com/t/easyroads3d-v3-the-upcoming-new-road-system-part-2/1487279/450 