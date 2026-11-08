// Copyright (c) Tailscale Inc & contributors
// SPDX-License-Identifier: BSD-3-Clause
// DemoMux Windows compatibility patch, based on Tailscale v1.104.5.

package atomicfile

import (
    "os"
    "path/filepath"
    "strings"

    "golang.org/x/sys/windows"
)

func rename(srcFile, destFile string) error {
    err := replaceFile(destFile, srcFile)
    if err == nil {
        return nil
    }
    if err == windows.ERROR_FILE_NOT_FOUND {
        err = os.Rename(srcFile, destFile)
        if err == nil { return nil }
        if pathError, ok := err.(*os.LinkError); ok { err = pathError.Err }
    }
    // ReplaceFileW may fail while merging EFS metadata even when both files
    // are readable and writable. Only use atomic rename when both files are
    // encrypted, in the same directory, and have identical DACLs (or when
    // creating a new file inside an encrypted directory). Windows can require
    // COPY_ALLOWED for EFS moves even on one volume; no decryption is requested.
    if (err == windows.ERROR_ACCESS_DENIED || err == windows.ERROR_NOT_SAME_DEVICE) && strings.EqualFold(filepath.Dir(srcFile), filepath.Dir(destFile)) {
        src, srcErr := windows.UTF16PtrFromString(srcFile)
        dst, dstErr := windows.UTF16PtrFromString(destFile)
        if srcErr == nil && dstErr == nil {
            srcAttr, srcErr := windows.GetFileAttributes(src)
            dstAttr, dstErr := windows.GetFileAttributes(dst)
            if srcErr == nil && dstErr == windows.ERROR_FILE_NOT_FOUND && srcAttr&windows.FILE_ATTRIBUTE_ENCRYPTED != 0 {
                parent, _ := windows.UTF16PtrFromString(filepath.Dir(destFile))
                parentAttr, parentErr := windows.GetFileAttributes(parent)
                if parentErr == nil && parentAttr&windows.FILE_ATTRIBUTE_ENCRYPTED != 0 {
                    return windows.MoveFileEx(src, dst, windows.MOVEFILE_REPLACE_EXISTING|windows.MOVEFILE_COPY_ALLOWED|windows.MOVEFILE_WRITE_THROUGH)
                }
            }
            if srcErr == nil && dstErr == nil && srcAttr&windows.FILE_ATTRIBUTE_ENCRYPTED != 0 && dstAttr&windows.FILE_ATTRIBUTE_ENCRYPTED != 0 {
                srcSD, srcErr := windows.GetNamedSecurityInfo(srcFile, windows.SE_FILE_OBJECT, windows.DACL_SECURITY_INFORMATION)
                dstSD, dstErr := windows.GetNamedSecurityInfo(destFile, windows.SE_FILE_OBJECT, windows.DACL_SECURITY_INFORMATION)
                if srcErr == nil && dstErr == nil && srcSD.String() == dstSD.String() {
                    return windows.MoveFileEx(src, dst, windows.MOVEFILE_REPLACE_EXISTING|windows.MOVEFILE_COPY_ALLOWED|windows.MOVEFILE_WRITE_THROUGH)
                }
            }
        }
    }
    return err
}

func replaceFile(destFile, srcFile string) error {
    dest, err := windows.UTF16PtrFromString(destFile)
    if err != nil { return err }
    src, err := windows.UTF16PtrFromString(srcFile)
    if err != nil { return err }
    return replaceFileW(dest, src, nil, 0, nil, nil)
}
