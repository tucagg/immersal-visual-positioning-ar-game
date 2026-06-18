# Immersal Visual Positioning AR Game

A location-based augmented reality application that leverages **Immersal Visual Positioning System (VPS)** to enable persistent, spatially accurate virtual content anchored to real-world environments.

---

## Overview

This project implements a detective-style augmented reality experience where digital objects are permanently attached to mapped physical locations.

Unlike GPS-based AR systems, positioning is achieved through **visual localization**, enabling centimeter-level spatial alignment and stable anchor persistence across sessions.

The system demonstrates how real-world spatial mapping and visual positioning technologies can be integrated into interactive gameplay mechanics.

---

## Technical Architecture

**Engine:** Unity  
**AR Framework:** AR Foundation  
**Positioning System:** Immersal VPS (Visual Localization & Mapping)  
**Backend Services:** Firebase (Authentication & Data Management)  
**Programming Language:** C#

### Core Components

- Real-world environment scanning and map generation using Immersal
- Cloud-based visual localization
- Persistent spatial anchor management
- Session-independent object realignment
- Location-triggered gameplay logic

---

## Required Unity Asset Store Packages

This repository excludes licensed third-party Unity Asset Store content.  
Before running the project, please download and import the following assets:

- [Simple Stylized Cardboard Boxes](https://assetstore.unity.com/packages/3d/props/simple-stylized-cardboard-boxes-308830)
- [Simple Keys](https://assetstore.unity.com/packages/3d/props/tools/simple-keys-231162)
- [Money Pack](https://assetstore.unity.com/packages/3d/props/money-pack-84433)
- [PKS Laptop Low](https://assetstore.unity.com/packages/3d/props/pks-laptop-low-264665)
- [Low Poly Stylized Knife Pack](https://assetstore.unity.com/packages/3d/props/weapons/low-poly-stylized-knife-pack-299272)
- [Moka Pot and Espresso Coffee Cup](https://assetstore.unity.com/packages/3d/props/moka-pot-and-espresso-coffee-cup-334456)
- [Mobile Books](https://assetstore.unity.com/packages/3d/props/interior/mobile-books-3356)

After downloading, place the imported asset folders inside the project's `Assets/` directory according to the expected structure.

---

## Developer Notes

### Adding a New Clue Prefab

When you create a new clue object type (e.g. `KeyCluePrefab`, `NoteCluePrefab`), follow these steps so it becomes tappable and draggable in the AR editing session:

1. Open the prefab in **Prefab Edit Mode** (double-click it in the Project window).
2. Make sure the root `GameObject` has a **Collider** component (e.g. `BoxCollider`). Without a collider, pointer events will not fire.
3. Add the **`AnchorHandle`** script to the same `GameObject`: `Inspector → Add Component → AnchorHandle`.
4. Match the **layer and tag** of the root object to those used by `CubeCluePrefab` (the reference prefab that already works).
5. Save the prefab.
6. In the scene, select the **`ImmersalSDK`** root object and find the **`AnchorsRealtime`** component. Add the new prefab to its **prefab list** (the slot that holds the pool of spawnable clue prefabs).
7. Open the **clue type dropdown** in the clue editing UI (`Screen_EditClue`) and add a matching entry for the new prefab key so creators can select it from the dropdown.

---

### Adding a New Immersal Map

To register a new physical environment so the app can localize inside it:

1. Under **`XRSpace`** in the scene hierarchy, find an existing `XRMap_*` object (the one that contains an **`XR Map`** script component). **Duplicate** it (`Ctrl/Cmd + D`).
2. Select the duplicate. In the `XR Map` component in the Inspector, click **Reconfigure**, enter the new map's **Immersal Map ID**, and enable all three checkboxes as needed. You can remove (or leave unchecked) the **Visualization** mesh — it is only needed during development.
3. **Important:** The **`GameRoot`** (i.e. the `ImmersalSDK` root object in the scene) must be **active** in the hierarchy while you configure the map. If it is disabled, the SDK cannot reach its credentials and the reconfigure step will fail.
4. If prompted, download the map data from the [Immersal Developer Portal](https://developers.immersal.com) and place it in the expected assets folder before reconfiguring.

---

## Documentation

Detailed technical documentation and system design:

[Project Documentation (PDF)](https://drive.google.com/file/d/1_kBqJC2LjE17_T3sFgz2HIS3rYQngwai/view?usp=share_link)
