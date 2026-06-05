import os
import sys

def main():
    # Output filename
    output_filename = "all_files.txt"
    
    # Absolute paths to exclude
    script_path = os.path.abspath(sys.argv[0])
    output_path = os.path.abspath(output_filename)
    
    # Files and extensions to ignore
    ignored_filenames = {'bootstrap.min.css', 'bootstrap.min.css.map', '.gitattributes', '.gitignore', 'README.md', 'LICENSE', 'bootstrap-grid.css',
    'bootstrap-grid.css.map', 'bootstrap-grid.min.css', 'bootstrap-grid.min.css.map', 'bootstrap-grid.rtl.css', 'bootstrap-grid.rtl.css.map',
    'bootstrap-grid.rtl.min.css', 'bootstrap-grid.rtl.min.css.map', 'bootstrap-reboot.css', 'bootstrap-reboot.css.map', 'bootstrap-reboot.min.css',
    'bootstrap-reboot.min.css.map', 'bootstrap-reboot.rtl.css', 'bootstrap-reboot.rtl.css.map', 'bootstrap-reboot.rtl.min.css', 'bootstrap-reboot.rtl.min.css.map',
    'bootstrap-utilities.css', 'bootstrap-utilities.css.map', 'bootstrap-utilities.min.css', 'bootstrap-utilities.min.css.map', 'bootstrap-utilities.rtl.css',
    'bootstrap-utilities.rtl.css.map', 'bootstrap-utilities.rtl.min.css', 'bootstrap-utilities.rtl.min.css.map', 'bootstrap.css', 'bootstrap.css.map',
    'bootstrap.rtl.css', 'bootstrap.rtl.css.map', 'bootstrap.rtl.min.css', 'bootstrap.rtl.min.css.map', 'bootstrap.bundle.js', 'bootstrap.bundle.js.map',
    'bootstrap.bundle.min.js', 'bootstrap.bundle.min.js.map', 'bootstrap.esm.js', 'bootstrap.esm.js.map', 'bootstrap.esm.min.js', 'bootstrap.esm.min.js.map',
    'bootstrap.js', 'bootstrap.js.map', 'bootstrap.min.js', 'bootstrap.min.js.map', 'appsettings.json'}
    ignored_extensions = {'.png', '.ttf', '.svg', '.ico'}
    
    with open(output_filename, 'w', encoding='utf-8') as outfile:
        for root, dirs, files in os.walk('.'):
            # Ignore specific directories (case-sensitive)
            dirs[:] = [d for d in dirs if d not in ('bin', 'obj', '.vs', '.git', 'dist', 'res')]
            
            for filename in files:
                filepath = os.path.join(root, filename)
                abs_path = os.path.abspath(filepath)
                
                # Skip script and output file
                if abs_path == script_path or abs_path == output_path:
                    continue
                
                # Skip specific files
                if filename in ignored_filenames:
                    continue
                
                # Skip files with ignored extensions (case-insensitive)
                _, ext = os.path.splitext(filename)
                if ext.lower() in ignored_extensions:
                    continue
                
                # Format path: replace separators and add leading slash
                rel_path = os.path.relpath(filepath, '.')
                formatted_path = '/' + rel_path.replace('\\', '/')
                
                # Read file content with encoding fallback
                try:
                    with open(filepath, 'r', encoding='utf-8') as f:
                        content = f.read()
                except UnicodeDecodeError:
                    try:
                        with open(filepath, 'r', encoding='latin-1') as f:
                            content = f.read()
                    except Exception as e:
                        print(f"Error reading {filepath}: {str(e)}", file=sys.stderr)
                        content = "[Failed to read file content]"
                
                # Write to output file
                outfile.write(f"Path: {formatted_path}\nContent:\n")
                outfile.write(content)
                outfile.write("\n\n\n------\n\n\n")

if __name__ == "__main__":
    main()