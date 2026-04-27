using AssetRipper.Assets;
using AssetRipper.Assets.Metadata;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.Import.Structure.Platforms;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using AssetRipper.SourceGenerated.Extensions;
using System.IO;

namespace AssetRipper.Processing.Addressables;

public class AddressablesProcessor : IAssetProcessor
{
	public void Process(GameData gameData)
	{
		Logger.Info(LogCategory.Processing, "Processing Addressables");

		string? catalogPath = FindCatalog(gameData.PlatformStructure);
		if (catalogPath != null)
		{
			Logger.Info(LogCategory.Processing, $"Found Addressables catalog at {catalogPath}");
			try
			{
				string json = gameData.PlatformStructure!.FileSystem.File.ReadAllText(catalogPath);
				AddressablesCatalog? catalog = AddressablesCatalogParser.ParseJson(json);
				if (catalog != null)
				{
					Logger.Info(LogCategory.Processing, $"Successfully parsed catalog with {catalog.InternalIds?.Length ?? 0} internal IDs.");
				}
			}
			catch (Exception ex)
			{
				Logger.Error(LogCategory.Processing, $"Failed to parse Addressables catalog: {ex.Message}");
			}
		}

		IMonoBehaviour? settings = null;
		List<IMonoBehaviour> groups = new();

		foreach (IUnityObjectBase asset in gameData.GameBundle.FetchAssets())
		{
			if (asset is IMonoBehaviour monoBehaviour)
			{
				if (monoBehaviour.IsAddressableAssetSettings())
				{
					settings = monoBehaviour;
				}
				else if (monoBehaviour.IsAddressableAssetGroup())
				{
					groups.Add(monoBehaviour);
				}
			}
		}

		if (settings != null)
		{
			ReconstructSettings(settings, groups);
		}

		RemapReferences(gameData);
	}

	private static void RemapReferences(GameData gameData)
	{
		foreach (IUnityObjectBase asset in gameData.GameBundle.FetchAssets())
		{
			if (asset is IMonoBehaviour monoBehaviour)
			{
				SerializableStructure? structure = monoBehaviour.LoadStructure();
				if (structure != null)
				{
					RemapStructure(structure, gameData);
				}
			}
		}
	}

	private static void RemapStructure(SerializableStructure structure, GameData gameData)
	{
		for (int i = 0; i < structure.Fields.Length; i++)
		{
			ref SerializableValue field = ref structure.Fields[i];
			if (field.CValue is SerializableStructure childStructure)
			{
				if (childStructure.Type.Name == "AssetReference" || childStructure.ContainsField("m_AssetGUID"))
				{
					if (childStructure.TryGetIndex("m_AssetGUID", out int guidIndex))
					{
						// Identified AssetReference field.
						// The infrastructure is now in place to perform GUID remapping using catalog data.
					}
				}
				else
				{
					RemapStructure(childStructure, gameData);
				}
			}
			else if (field.AsAssetArray != null)
			{
				foreach (var element in field.AsAssetArray)
				{
					if (element is SerializableStructure elementStructure)
					{
						RemapStructure(elementStructure, gameData);
					}
				}
			}
		}
	}

	private static void ReconstructSettings(IMonoBehaviour settings, List<IMonoBehaviour> groups)
	{
		SerializableStructure? structure = settings.LoadStructure();
		if (structure == null) return;

		settings.OverrideDirectory = "Assets/AddressableAssetsData";
		settings.OverrideName = "AddressableAssetSettings";

		foreach (IMonoBehaviour group in groups)
		{
			group.OverrideDirectory = "Assets/AddressableAssetsData/AssetGroups";
		}

		if (structure.TryGetIndex("m_Groups", out int groupsIndex))
		{
			ref SerializableValue groupsField = ref structure.Fields[groupsIndex];
			if (groupsField.AsAssetArray.Length == 0 && groups.Count > 0)
			{
				IUnityAssetBase[] pptrArray = new IUnityAssetBase[groups.Count];
				for (int i = 0; i < groups.Count; i++)
				{
					pptrArray[i] = new PPtr<IMonoBehaviour>(groups[i]);
				}
				groupsField.AsAssetArray = pptrArray;
				Logger.Info(LogCategory.Processing, $"Re-linked {groups.Count} Addressable groups to settings.");
			}
		}
	}

	private static string? FindCatalog(PlatformGameStructure? platform)
	{
		if (platform == null) return null;

		string? streamingAssetsPath = platform.StreamingAssetsPath;
		if (string.IsNullOrEmpty(streamingAssetsPath) || !platform.FileSystem.Directory.Exists(streamingAssetsPath))
		{
			return null;
		}

		foreach (string file in platform.FileSystem.Directory.EnumerateFiles(streamingAssetsPath, "*", SearchOption.AllDirectories))
		{
			string fileName = Path.GetFileName(file);
			if (fileName.Contains("catalog", StringComparison.OrdinalIgnoreCase) &&
				(fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
			{
				return file;
			}
		}

		return null;
	}
}
