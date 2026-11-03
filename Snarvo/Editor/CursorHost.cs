// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Snarvo.Editor;

/// <summary>A Grid whose mouse cursor can be changed from outside (ProtectedCursor is protected).</summary>
public sealed partial class CursorHost : Grid
{
    InputSystemCursorShape? _shape;

    public void SetCursor(InputSystemCursorShape shape)
    {
        if (_shape == shape) return;
        _shape = shape;
        ProtectedCursor = InputSystemCursor.Create(shape);
    }
}
