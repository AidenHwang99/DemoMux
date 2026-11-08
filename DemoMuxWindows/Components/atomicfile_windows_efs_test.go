//go:build windows

package atomicfile

import (
    "os"
    "path/filepath"
    "testing"

    "golang.org/x/sys/windows"
)

func TestDemoMuxEFSAtomicReplace(t *testing.T) {
    root := os.Getenv("DEMOMUX_EFS_TEST_DIR")
    if root == "" { t.Skip("set DEMOMUX_EFS_TEST_DIR to an existing encrypted test directory") }
    dir, err := os.MkdirTemp(root, "atomic-regression-")
    if err != nil { t.Fatal(err) }
    defer os.RemoveAll(dir)
    path := filepath.Join(dir, "state.json")
    if err := WriteFile(path, []byte("first"), 0600); err != nil { t.Fatal(err) }
    name, _ := windows.UTF16PtrFromString(path)
    before, err := windows.GetFileAttributes(name)
    if err != nil || before&windows.FILE_ATTRIBUTE_ENCRYPTED == 0 { t.Fatal("test fixture must be EFS encrypted", err) }
    security, err := windows.GetNamedSecurityInfo(path, windows.SE_FILE_OBJECT, windows.DACL_SECURITY_INFORMATION)
    if err != nil { t.Fatal(err) }
    if err := WriteFile(path, []byte("second"), 0600); err != nil { t.Fatal(err) }
    data, err := os.ReadFile(path)
    if err != nil || string(data) != "second" { t.Fatal("replacement contents", err) }
    after, err := windows.GetFileAttributes(name)
    if err != nil || after&windows.FILE_ATTRIBUTE_ENCRYPTED == 0 { t.Fatal("encryption was lost", err) }
    updated, err := windows.GetNamedSecurityInfo(path, windows.SE_FILE_OBJECT, windows.DACL_SECURITY_INFORMATION)
    if err != nil || updated.String() != security.String() { t.Fatal("access permissions changed", err) }
    t.Log("EFS contents replaced; encryption and DACL preserved")
}
