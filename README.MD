# ⚔️ ARena Clash — Mobile AR Card Combat Game

An interactive Augmented Reality (AR) mobile game built with **Unity 6**, **Universal Render Pipeline (URP)**, and **AR Foundation**. The project utilizes physical card markers to render animated 3D weapons and characters directly on top of real-world playing cards.

## 📌 Features
- **Card Tracking**: Detects specific playing cards and summons 3D models (e.g., Swords, Axes, Knights).
- **Auto-Scaling**: Models automatically scale and position themselves based on the physical card size.
- **Animations**: Hover and rotation effects for active weapons.
- **Cross-Platform Support**: Built for Android (ARCore), with PC testing enabled via XR Simulation.

## 🛠️ Requirements
- **Unity Engine**: `6000.x` (Unity 6)
- **Target Platform**: Android (API Level 24+)
- **Mobile Device**: ARCore-compatible Android phone.
- **Markers**: Physical playing cards or printed images to serve as AR targets.

## 📦 Required Unity Packages

Install the following official packages via **Window > Package Manager** (Unity Registry):

| Package Name                | Min Version | Description                                      |
| --------------------------- | ----------- | ------------------------------------------------ |
| **AR Foundation**           | `6.0.x`     | Core AR framework for cross-platform tracking    |
| **Google ARCore XR Plugin** | `6.0.x`     | Native Android ARCore provider plugin            |
| **XR Plug-in Management**   | `4.x`       | Architecture for managing XR provider lifecycles |

## 🚀 Getting Started
1. Clone this repository:
   ```bash
   git clone https://github.com/aqueiros22/RVA-PROJECT.git
   ```
2. Open the project in Unity 6.
3. Open **Window > Package Manager**.
4. Set the dropdown menu (*Packages:*) to **Unity Registry**.
5. Search for and install **AR Foundation** and **Google ARCore XR Plugin**.
6. Switch the build platform to **Android** (`File > Build Settings`).
7. Connect your Android device (with USB Debugging enabled) and click **Build and Run**.
