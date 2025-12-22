using System;
using System.Numerics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Lumina.Data.Files;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using static Lumina.Data.Parsing.Uld.UldRoot;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using TickTracker.Helpers;

namespace TickTracker.NativeNodes;

public sealed unsafe class ImageNode : IDisposable
{
    public required uint NodeId { get; set; }
    public NodeType Type { get; } = NodeType.Image;
    public NodeFlags NodeFlags { get; set; } = NodeFlags.AnchorTop | NodeFlags.AnchorLeft | NodeFlags.Enabled | NodeFlags.Visible | NodeFlags.EmitsEvents;
    public uint DrawFlags { get; set; } = 1;
    public AtkImageNode* imageNode { get; private set; } = null;
    public int atkUldPartsListsAvailable { get; init; }
    private bool isDisposed;

    private readonly IDataManager dataManager;
    private readonly IPluginLog log;
    private readonly PartsData[] uldPartsListArray;

    private readonly Dictionary<uint, string> textureDictionary = [];
    private readonly Dictionary<uint, string> hqTextureDictionary = [];
    private Vector2 nodePosition = new(-1, -1);
    private AtkResNode* imageNodeParent;

    public ImageNode(IDataManager _dataManager, IPluginLog _log, UldWrapper uld)
    {
        dataManager = _dataManager;
        log = _log;

        if (!uld.Valid || uld.Uld is null)
        {
            throw new ArgumentException("UldWrapper provided isn't valid or it has a null UldFile", nameof(uld));
        }
        ParseUld(uld.Uld, out var uldPartsListAmount, out uldPartsListArray);
        atkUldPartsListsAvailable = uldPartsListAmount;
    }

    private void ParseUld(UldFile uld, out int uldPartsListAmount, out PartsData[] uldPartsListArray)
    {
        uldPartsListAmount = uld.Parts.Length;
        uldPartsListArray = uld.Parts;

        textureDictionary.Clear();
        hqTextureDictionary.Clear();
        for (var i = 0; i < uld.AssetData.Length; i++)
        {
            var rawTexturePath = uld.AssetData[i].Path;
            if (rawTexturePath is null)
            {
                continue;
            }

            var textureId = uld.AssetData[i].Id;
            var texturePath = new string(rawTexturePath, 0, rawTexturePath.Length).Trim('\0').Trim();
            var hqTexturePath = texturePath.Replace(".tex", "_hr1.tex", StringComparison.Ordinal);

            if (dataManager.FileExists(texturePath))
            {
                textureDictionary.Add(textureId, texturePath);
            }
            if (dataManager.FileExists(hqTexturePath))
            {
                hqTextureDictionary.Add(textureId, hqTexturePath);
            }
        }
    }

    /// <summary>
    /// Creates a complete <see cref="AtkUldPartsList"/> pointer array from the provided <see cref="UldWrapper"/>.
    /// </summary>
    /// <remarks>
    /// <b>Must be disposed with <see cref="FreeResources(AtkUldPartsList*[])"/></b>
    /// </remarks>
    /// <returns></returns>
    public AtkUldPartsList*[]? CreateAtkUldPartsListArray(bool hqTexture = true)
    {
        if (uldPartsListArray is [] || (textureDictionary.Count is 0 && !hqTexture) || (hqTextureDictionary.Count is 0 && hqTexture))
        {
            return null;
        }
        var atkUldPartsListArray = new AtkUldPartsList*[atkUldPartsListsAvailable];
        for (var i = 0; i < atkUldPartsListArray.Length; i++)
        {
            var currentAtkPartsList = (AtkUldPartsList*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldPartsList), 8);
            if (currentAtkPartsList is null)
            {
                FreeResources(atkUldPartsListArray, i - 1, increase: false);
                return null;
            }
            currentAtkPartsList->Id = uldPartsListArray[i].Id;
            currentAtkPartsList->PartCount = uldPartsListArray[i].PartCount;
            var currentAtkPartList = (AtkUldPart*)IMemorySpace.GetUISpace()->Malloc((ulong)(sizeof(AtkUldPart) * currentAtkPartsList->PartCount), 8);
            if (currentAtkPartList is null)
            {
                IMemorySpace.Free(currentAtkPartsList, (ulong)sizeof(AtkUldPartsList));
                FreeResources(atkUldPartsListArray, i - 1, increase: false);
                return null;
            }
            currentAtkPartsList->Parts = currentAtkPartList;
            if (!PopulatePartsList(ref currentAtkPartsList, uldPartsListArray[i], hqTexture))
            {
                IMemorySpace.Free(currentAtkPartList, (ulong)(sizeof(AtkUldPart) * currentAtkPartsList->PartCount));
                IMemorySpace.Free(currentAtkPartsList, (ulong)sizeof(AtkUldPartsList));
                FreeResources(atkUldPartsListArray, i - 1, increase: false);
                return null;
            }
            atkUldPartsListArray[i] = currentAtkPartsList;
        }
        return atkUldPartsListArray;
    }

    private bool PopulatePartsList(ref AtkUldPartsList* currentPartsList, PartsData currentUldPartsList, bool hqTexture)
    {
        for (var i = 0; i < currentPartsList->PartCount; i++)
        {
            var currentAsset = (AtkUldAsset*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldAsset), 8);
            if (currentAsset is null)
            {
                for (var j = i - 1; j >= 0; j--)
                {
                    currentPartsList->Parts[j].UldAsset->AtkTexture.ReleaseTexture();
                    currentPartsList->Parts[j].UldAsset->AtkTexture.Destroy(free: false);
                    IMemorySpace.Free(currentPartsList->Parts[j].UldAsset, (ulong)sizeof(AtkUldAsset));
                }
                return false;
            }
            currentPartsList->Parts[i].U = currentUldPartsList.Parts[i].U;
            currentPartsList->Parts[i].V = currentUldPartsList.Parts[i].V;
            currentPartsList->Parts[i].Width = currentUldPartsList.Parts[i].W;
            currentPartsList->Parts[i].Height = currentUldPartsList.Parts[i].H;
            currentAsset->Id = currentUldPartsList.Parts[i].TextureId;
            currentAsset->AtkTexture.Ctor();
            if (hqTexture && hqTextureDictionary.TryGetValue(currentAsset->Id, out var hqTexturePath))
            {
                currentAsset->AtkTexture.LoadTexture(hqTexturePath);
            }
            else if (!hqTexture && textureDictionary.TryGetValue(currentAsset->Id, out var texturePath))
            {
                currentAsset->AtkTexture.LoadTexture(texturePath);
            }
            currentPartsList->Parts[i].UldAsset = currentAsset;
        }
        return true;
    }

    private AtkUldPartsList* GetImagePartsList(int partsListIndex, bool hqTexture)
    {
        var imageNodePartsList = (AtkUldPartsList*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldPartsList), 8);
        if (imageNodePartsList is null)
        {
            log.Error("Memory for the node AtkUldPartsList could not be allocated.");
            return null;
        }
        imageNodePartsList->PartCount = uldPartsListArray[partsListIndex].PartCount;
        imageNodePartsList->Id = uldPartsListArray[partsListIndex].Id;
        var imageNodePartList = (AtkUldPart*)IMemorySpace.GetUISpace()->Malloc((ulong)(sizeof(AtkUldPart) * imageNodePartsList->PartCount), 8);
        if (imageNodePartList is null)
        {
            IMemorySpace.Free(imageNodePartsList, (ulong)sizeof(AtkUldPartsList));
            log.Error("Memory for the node AtkUldParts could not be allocated.");
            return null;
        }
        imageNodePartsList->Parts = imageNodePartList;
        for (var i = 0; i < imageNodePartsList->PartCount; i++)
        {
            var currentAsset = (AtkUldAsset*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldAsset), 8);
            if (currentAsset is null)
            {
                log.Error("Memory for the node AtkUldAsset of AtkUldPart {i} could not be allocated.", i);
                for (var j = i - 1; j >= 0; j--)
                {
                    imageNodePartsList->Parts[j].UldAsset->AtkTexture.ReleaseTexture();
                    imageNodePartsList->Parts[j].UldAsset->AtkTexture.Destroy(free: false);
                    IMemorySpace.Free(imageNodePartsList->Parts[j].UldAsset, (ulong)sizeof(AtkUldAsset));
                }
                IMemorySpace.Free(imageNodePartsList->Parts, (ulong)(sizeof(AtkUldPart) * imageNodePartsList->PartCount));
                IMemorySpace.Free(imageNodePartsList, (ulong)sizeof(AtkUldPartsList));
                return null;
            }

            imageNodePartsList->Parts[i].U = uldPartsListArray[partsListIndex].Parts[i].U;
            imageNodePartsList->Parts[i].V = uldPartsListArray[partsListIndex].Parts[i].V;
            imageNodePartsList->Parts[i].Width = uldPartsListArray[partsListIndex].Parts[i].W;
            imageNodePartsList->Parts[i].Height = uldPartsListArray[partsListIndex].Parts[i].H;
            currentAsset->Id = uldPartsListArray[partsListIndex].Parts[i].TextureId;
            currentAsset->AtkTexture.Ctor();
            if (hqTexture && hqTextureDictionary.TryGetValue(currentAsset->Id, out var hqTexturePath))
            {
                currentAsset->AtkTexture.LoadTexture(hqTexturePath);
            }
            else if (!hqTexture && textureDictionary.TryGetValue(currentAsset->Id, out var texturePath))
            {
                currentAsset->AtkTexture.LoadTexture(texturePath);
            }
            imageNodePartsList->Parts[i].UldAsset = currentAsset;
        }

        return imageNodePartsList;
    }

    /// <summary>
    /// Creates a new <see cref="AtkImageNode"/>, that can be accessed through <see cref="imageNode"/>, with a <see cref="AtkUldPartsList"/> created from the provided <see cref="UldWrapper"/>.
    /// </summary>
    /// <remarks>
    /// The existing <see cref="imageNode"/> is destroyed if present.
    /// </remarks>
    /// <param name="partsListIndex">The index of the <see cref="AtkUldPartsList"/> to fetch from the <see cref="uldPartsListArray"/></param>
    /// <param name="hqTexture"> Whether to retrieve the hr1 version of the contained textures, or the normal one.</param>
    /// <param name="parent">Optional <see cref="AtkComponentNode"/> that can be used to attach the created <see cref="imageNode"/></param>
    /// <param name="targetNode">The <see cref="AtkResNode"/> after the desired position to place our <see cref="imageNode"/></param>
    public void CreateCompleteImageNode(int partsListIndex, bool hqTexture = false, AtkResNode* parent = null, AtkResNode* targetNode = null)
    {
        DestroyNode();
        imageNode = IMemorySpace.GetUISpace()->Create<AtkImageNode>();
        if (imageNode is null || partsListIndex > uldPartsListArray.Length - 1)
        {
            log.Error("Memory for the node could not be allocated or index is out of bounds.");
            return;
        }
        var atkPartsList = GetImagePartsList(partsListIndex, hqTexture);
        if (atkPartsList is null)
        {
            IMemorySpace.Free(imageNode, (ulong)sizeof(AtkImageNode));
            log.Error("Could not create AtkUldPartsList for image node.");
            return;
        }
        if (parent is not null && targetNode is null)
        {
            log.Error("targetNode must be provided!");
        }

        if (parent is null && targetNode is not null)
        {
            log.Error("parent must be provided!");
        }

        imageNode->AtkResNode.Type = Type;
        imageNode->AtkResNode.NodeId = NodeId;
        imageNode->PartsList = atkPartsList;

        imageNode->PartId = 0;
        imageNode->AtkResNode.NodeFlags = NodeFlags;
        imageNode->AtkResNode.DrawFlags = DrawFlags;
        imageNode->WrapMode = 1;
        imageNode->Flags = (ImageNodeFlags)0;
        imageNode->AtkResNode.SetScale(1, 1);
        imageNode->AtkResNode.ToggleVisibility(enable: true);

        if (parent is not null && targetNode is not null)
        {
            imageNodeParent = parent;
            NativeUi.LinkNodeAfterTargetNode(&imageNode->AtkResNode, parent->GetAsAtkComponentNode(), targetNode);
        }
        nodePosition.X = imageNode->AtkResNode.GetXFloat();
        nodePosition.Y = imageNode->AtkResNode.GetYFloat();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetNodePosition(float X, float Y)
    {
        imageNode->AtkResNode.SetXFloat(X);
        imageNode->AtkResNode.SetYFloat(Y);
    }

    public void HideNode()
    {
        if (imageNode is null || !imageNode->AtkResNode.IsVisible())
        {
            return;
        }
        imageNode->AtkResNode.ToggleVisibility(enable: false);
    }

    public void ResetNodePosition()
    {
        if (nodePosition == new Vector2(-1, -1))
        {
            log.Error("Image node not initialised");
            return;
        }
        imageNode->AtkResNode.SetXFloat(nodePosition.X);
        imageNode->AtkResNode.SetYFloat(nodePosition.Y);
    }

    /// <summary>
    /// Changes the <see cref="imageNode"/> texture color using the provided vector.
    /// </summary>
    /// <remarks>
    ///     <paramref name="Color"/> must contain values only from 0 to 1.
    /// </remarks>
    public void ChangeNodeColorAndAlpha(Vector4 Color)
    {
        if (Color.X > 1 || Color.Y > 1 || Color.Z > 1 || Color.W > 1)
        {
            return;
        }
        if (ColorEquals(imageNode->AtkResNode.Color, Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(Color)))
        {
            return;
        }
        imageNode->AtkResNode.Color = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(Color);
        imageNode->AtkResNode.MultiplyRed = (byte)(255 * Color.X);
        imageNode->AtkResNode.MultiplyGreen = (byte)(255 * Color.Y);
        imageNode->AtkResNode.MultiplyBlue = (byte)(255 * Color.Z);
        imageNode->AtkResNode.SetAlpha((byte)(255 * Color.W));
    }

    public static bool ColorEquals(ByteColor nodeColor, ByteColor comparisonColor)
        => nodeColor.R == comparisonColor.R
        && nodeColor.G == comparisonColor.G
        && nodeColor.B == comparisonColor.B
        && nodeColor.A == comparisonColor.A;

    public void ChangePartsList(int partsListIndex, bool hqTexture = false, ushort partId = 0)
    {
        if (partsListIndex > uldPartsListArray.Length - 1)
        {
            log.Error("partsListIndex out of bounds");
            return;
        }
        var desiredPartsList = GetImagePartsList(partsListIndex, hqTexture);
        if (desiredPartsList is null)
        {
            log.Error("The desired partsList could not be created");
            return;
        }
        DestroyImagePartsList();
        imageNode->PartsList = desiredPartsList;
        imageNode->PartId = partId;
    }

    private void DestroyImagePartsList()
    {
        for (var i = 0; i < imageNode->PartsList->PartCount; i++)
        {
            imageNode->PartsList->Parts[i].UldAsset->AtkTexture.ReleaseTexture();
            imageNode->PartsList->Parts[i].UldAsset->AtkTexture.Destroy(free: false);
            IMemorySpace.Free(imageNode->PartsList->Parts[i].UldAsset, (ulong)sizeof(AtkUldAsset));
        }
        IMemorySpace.Free(imageNode->PartsList->Parts, (ulong)(sizeof(AtkUldPart) * imageNode->PartsList->PartCount));
        IMemorySpace.Free(imageNode->PartsList, (ulong)sizeof(AtkUldPartsList));
    }

    /// <summary>
    /// Destroys the current <see cref="AtkImageNode"/>, if it exists.
    /// </summary>
    public void DestroyNode()
    {
        if (imageNode is null)
        {
            return;
        }
        if (imageNodeParent is not null)
        {
            NativeUi.UnlinkNode(imageNode, imageNodeParent->GetComponent());
        }
        DestroyImagePartsList();
        imageNode->AtkResNode.Destroy(free: false);
        IMemorySpace.Free(imageNode, (ulong)sizeof(AtkImageNode));
        imageNode = null;
        imageNodeParent = null;
    }

    /// <summary>
    /// Release a <see cref="AtkUldPartsList"/> pointer array created by <see cref="CreateAtkUldPartsListArray"/> of your instance of <see cref="ImageNode"/>.
    /// </summary>
    /// <param name="atkUldPartsListArray"></param>
    public void FreeResources(AtkUldPartsList*[] atkUldPartsListArray, int start = 0, bool increase = true)
    {
        void Operator(ref int i)
            => i = increase ? i + 1 : i - 1;
        bool Comparison(int a, int b)
            => increase ? a < b : a >= 0;

        for (var i = start; Comparison(i, atkUldPartsListArray.Length); Operator(ref i))
        {
            for (var j = 0; j < atkUldPartsListArray[i]->PartCount; j++)
            {
                atkUldPartsListArray[i]->Parts[j].UldAsset->AtkTexture.ReleaseTexture();
                atkUldPartsListArray[i]->Parts[j].UldAsset->AtkTexture.Destroy(free: false);
                IMemorySpace.Free(atkUldPartsListArray[i]->Parts[j].UldAsset, (ulong)sizeof(AtkUldAsset));
            }
            IMemorySpace.Free(atkUldPartsListArray[i]->Parts, (ulong)(sizeof(AtkUldPart) * atkUldPartsListArray[i]->PartCount));
            IMemorySpace.Free(atkUldPartsListArray[i], (ulong)sizeof(AtkUldPartsList));
        }
    }

    /// <summary>
    /// Unlinks and destroys the existing <see cref="AtkImageNode"/>, and frees the array of <see cref="AtkUldPartsList"/> created on class construction from the <see cref="UldWrapper"/>.
    /// </summary>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }
        if (imageNode is not null)
        {
            DestroyNode();
        }
        isDisposed = true;
    }
}
