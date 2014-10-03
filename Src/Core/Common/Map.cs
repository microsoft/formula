namespace Microsoft.Formula.Common
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Threading;

    /// <summary>
    /// A map backed by a red-black tree. Thread-safe only for reads.
    /// </summary>
    public class Map<S, T> : IEnumerable<KeyValuePair<S, T>>
    {
        private Comparison<S> comparer;

        private ThreadLocal<Node> lastFound = new ThreadLocal<Node>();

        private int numberOfKeys = 0;

        private LinkedList<Phantom> phantomList = new LinkedList<Phantom>();

        private Node root = null;

        public Map(Comparison<S> comparer)
        {
            this.comparer = comparer;
        }

        public Map(Map<S, T> map)
        {
            Contract.Requires(map != null);
            this.comparer = map.comparer;
            foreach (var kv in map)
            {
                this[kv.Key] = kv.Value;
            }
        }

        /// <summary>
        /// Reverse sorted-order enumeration.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public IEnumerable<KeyValuePair<S, T>> Reverse
        {
            get
            {
                if (this.root == null)
                {
                    yield break;
                }
                else
                {
                    var enumState = new Stack<Node>();
                    var crnt = root;
                    while (crnt != null)
                    {
                        enumState.Push(crnt);
                        crnt = (crnt.Right is Phantom) ? null : crnt.Right;
                    }

                    while (enumState.Count > 0)
                    {
                        crnt = enumState.Pop();
                        var result = new KeyValuePair<S, T>(crnt.Key, crnt.Value);
                        crnt = crnt.Left;
                        while (crnt != null)
                        {
                            enumState.Push(crnt);
                            crnt = (crnt.Right is Phantom) ? null : crnt.Right;
                        }

                        yield return result;
                    }
                }
            }
        }

        public IEnumerable<S> Keys
        {
            get
            {
                foreach (var kv in this)
                {
                    yield return kv.Key;
                }
            }
        }

        public IEnumerable<T> Values
        {
            get
            {
                foreach (var kv in this)
                {
                    yield return kv.Value;
                }
            }
        }

        public Comparison<S> Comparer
        {
            get
            {
                return comparer;
            }
        }

        public int Count
        {
            get
            {
                return numberOfKeys;
            }
        }

        public T this[S key]
        {
            get
            {
                T val;
                if (!TryFindValue(key, out val))
                {
                    throw new KeyNotFoundException(string.Format("Could not find {0}", key));
                }

                return val;
            }

            set
            {
                var n = FindNearestNode(key);
                if (n == null || Compare(n, key) != 0)
                {
                    Add(n, key, value);
                }
                else
                {
                    n.Value = value;
                }
            }
        }

        #region IEnumerable<KeyValuePair<S,T>> Members

        IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public IEnumerator<KeyValuePair<S, T>> GetEnumerator()
        {
            if (this.root == null)
            {
                yield break;
            }
            else
            {
                var enumState = new Stack<Node>();
                var crnt = root;
                while (crnt != null)
                {
                    enumState.Push(crnt);
                    crnt = (crnt.Left is Phantom) ? null : crnt.Left;
                }

                while (enumState.Count > 0)
                {
                    crnt = enumState.Pop();
                    var result = new KeyValuePair<S, T>(crnt.Key, crnt.Value);
                    crnt = crnt.Right;
                    while (crnt != null)
                    {
                        enumState.Push(crnt);
                        crnt = (crnt.Left is Phantom) ? null : crnt.Left;
                    }

                    yield return result;
                }
            }
        }

        #endregion

        public void Clear()
        {
            numberOfKeys = 0;
            phantomList.Clear();
            root = null;
            lastFound.Value = null;
        }

        [Pure]
        public bool ContainsKey(S key)
        {
            var n = FindNearestNode(key);
            return n != null && Compare(n, key) == 0;
        }

        /// <summary>
        /// Sets the value of an existing key, other throws an exception.
        /// Can be called while enumerating over keys.
        /// </summary>
        public void SetExistingKey(S key, T value)
        {
            var n = FindNearestNode(key);
            if (n == null || Compare(n, key) != 0)
            {
                throw new KeyNotFoundException();
            }
            else
            {
                n.Value = value;
            }
        }

        /// <summary>
        /// If k is a key, then returns true and the ordinal of k in the map domain.
        /// Otherwise, returns false.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool GetKeyOrdinal(S k, out int ordinal)
        {
            ordinal = 0;
            foreach (var kv in this)
            {
                if (comparer(kv.Key, k) == 0)
                {
                    return true;
                }

                ++ordinal;
            }

            return false;
        }

        /// <summary>
        /// Start sorted-order enumeration at the element k.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public IEnumerable<KeyValuePair<S, T>> GetEnumerable(S k)
        {
            if (this.root == null)
            {
                yield break;
            }

            var skip = new Set<S>(this.comparer);
            var enumState = new Stack<Node>();
            var crnt = root;
            int cmp;
            while (crnt != null)
            {
                enumState.Push(crnt);
                cmp = Compare(crnt, k);
                if (cmp == 0)
                {
                    break;
                }
                else if (cmp < 0)
                {
                    if (crnt.Right == null)
                    {
                        throw new Exception("Key not in collection");
                    }

                    skip.Add(crnt.Key);
                    crnt = crnt.Right;
                }
                else
                {
                    if (crnt.Left == null)
                    {
                        throw new Exception("Key not in collection");
                    }

                    crnt = crnt.Left;
                }
            }

            while (enumState.Count > 0)
            {
                crnt = enumState.Pop();
                if (skip.Contains(crnt.Key))
                {
                    continue;
                }

                var result = new KeyValuePair<S, T>(crnt.Key, crnt.Value);
                crnt = crnt.Right;
                while (crnt != null)
                {
                    enumState.Push(crnt);
                    crnt = (crnt.Left is Phantom) ? null : crnt.Left;
                }

                yield return result;
            }
        }


        /// <summary>
        /// Gets the largest key/value pair that is less than or equal to k and in the domain
        /// of the map. Returns false if there is no key in the map less than or equal to k.
        /// </summary>
        public bool GetGLB(S k, out S kGLB, out T vGLB)
        {
            Node n = root;
            int cmp;
            bool success = false;
            kGLB = default(S);
            vGLB = default(T);

            while (n != null)
            {
                Contract.Assert(!n.IsPhantomNode());
                cmp = Compare(n, k);
                if (cmp == 0)
                {
                    kGLB = n.Key;
                    vGLB = n.Value;
                    return true;
                }
                else if (cmp > 0)
                {
                    if (n.Left == null)
                    {
                        return success;
                    }

                    n = n.Left;
                }
                else
                {
                    kGLB = n.Key;
                    vGLB = n.Value;
                    success = true;
                    n = n.Right;
                }
            }

            return success;
        }

        /// <summary>
        /// Gets the smallest key/value pair that is greater than or equal to k and in the domain
        /// of the map. Returns false if there is no key in the map greater than or equal to k.
        /// </summary>
        public bool GetLUB(S k, out S kLUB, out T vLUB)
        {
            Node n = root;
            int cmp;
            bool success = false;
            kLUB = default(S);
            vLUB = default(T);

            while (n != null)
            {
                Contract.Assert(!n.IsPhantomNode());
                cmp = Compare(n, k);
                if (cmp == 0)
                {
                    kLUB = n.Key;
                    vLUB = n.Value;
                    return true;
                }
                else if (cmp > 0)
                {
                    kLUB = n.Key;
                    vLUB = n.Value;
                    success = true;
                    n = n.Left;
                }
                else
                {
                    if (n.Right == null)
                    {
                        return success;
                    }

                    n = n.Right;
                }
            }

            return success;
        }

        /// <summary>
        /// If K is the set of keys, then every pair (k1,k2) \in K^2
        /// is visited at most once. Additionally, for every set of pairs
        /// { (k1, k2), (k2,k1) } exactly one of the pairs in the set
        /// is visited.
        /// </summary>
        /// <param name="visitor">The key-pair visitor</param>
        public void VisitKeyPairs(Action<S, S> visitor)
        {
            VisitKeyPairs(this.root, visitor);
        }

        /// <summary>
        /// If there are one or more keys in the map, then this returns
        /// one of them. Otherwise, default(S) is returned. The key returned
        /// is arbitrary but not random.
        /// </summary>
        /// <returns>Some key or default(S)</returns>
        public S GetSomeKey()
        {
            return (root != null && !(root is Phantom)) ? root.Key : default(S);
        }

        public bool TryFindValue(S key, out T value)
        {
            if (lastFound.Value != null && Compare(lastFound.Value, key) == 0)
            {
                value = lastFound.Value.Value;
                return true;
            }

            Node checkNode = root;
            int cmp;
            while (checkNode != null)
            {
                cmp = Compare(checkNode, key);
                if (cmp == 0)
                {
                    value = checkNode.Value;
                    lastFound.Value = checkNode;
                    return true;
                }
                else if (cmp > 0)
                {
                    checkNode = checkNode.Left;
                }
                else
                {
                    checkNode = checkNode.Right;
                }
            }

            value = default(T);
            return false;
        }

        public bool Remove(S key)
        {
            lastFound.Value = null;
            Node deletePos = FindNearestNode(key);
            if (deletePos == null || Compare(deletePos, key) != 0)
            {
                return false;
            }
            else
            {
                deletePos = BinaryTreeReplace(deletePos);
            }

            --numberOfKeys;
            Node child = (deletePos.Left != null) ? deletePos.Left : ((deletePos.Right != null) ? deletePos.Right : new Phantom(this, deletePos, true));
            SwapNodes(deletePos, child);
            if (!deletePos.Red)
            {
                if (child.Red)
                {
                    child.Red = false;
                }
                else
                {
                    while (child.Parent != null)
                    {
                        Node childsib = null;
                        Node childSibL = null;
                        Node childSibR = null;
                        ConstructSiblingFamily(child, ref childsib, ref childSibL, ref childSibR);
                        if (childsib.Red)
                        {
                            child.Parent.Red = true;
                            childsib.Red = false;
                            if (child == child.Parent.Left)
                            {
                                LeftRotate(child.Parent);
                            }
                            else
                            {
                                RightRotate(child.Parent);
                            }

                            ConstructSiblingFamily(child, ref childsib, ref childSibL, ref childSibR);
                        }

                        if (!child.Parent.Red && !childSibL.Red && !childSibR.Red)
                        {
                            childsib.Red = true;
                            child = child.Parent;
                        }
                        else if (child.Parent.Red && !childsib.Red && !childSibL.Red && !childSibR.Red)
                        {
                            childsib.Red = true;
                            child.Parent.Red = false;
                            break;
                        }
                        else
                        {
                            if (child == child.Parent.Left && !childsib.Red && childSibL.Red && !childSibR.Red)
                            {
                                childsib.Red = true;
                                childSibL.Red = false;
                                RightRotate(childsib);
                                ConstructSiblingFamily(child, ref childsib, ref childSibL, ref childSibR);
                            }
                            else if (child == child.Parent.Right && !childsib.Red && childSibR.Red && !childSibL.Red)
                            {
                                childsib.Red = true;
                                childSibR.Red = false;
                                LeftRotate(childsib);
                                ConstructSiblingFamily(child, ref childsib, ref childSibL, ref childSibR);
                            }

                            childsib.Red = child.Parent.Red;
                            child.Parent.Red = false;
                            if (child == child.Parent.Left)
                            {
                                childSibR.Red = false;
                                LeftRotate(child.Parent);
                            }
                            else
                            {
                                childSibL.Red = false;
                                RightRotate(child.Parent);
                            }

                            break;
                        }
                    }
                }
            }

            deletePos.DiscardNode(this);
            ClearPhantoms();
            return true;
        }

        public void Add(S key, T value)
        {
            Add(FindNearestNode(key), key, value);
        }

        private void VisitKeyPairs(Node n, Action<S, S> visitor)
        {
            if (n == null || n is Phantom)
            {
                return;
            }

            VisitKeyPairs(n.Left, visitor);
            VisitKeyPairs(n, this.root, visitor);
            VisitKeyPairs(n.Right, visitor);
        }

        private void VisitKeyPairs(Node n1, Node n2, Action<S, S> visitor)
        {
            if (n2 == null || n2 is Phantom)
            {
                return;
            }

            var cmp = this.comparer(n1.Key, n2.Key);
            if (cmp < 0)
            {
                VisitKeyPairs(n1, n2.Left, visitor);
            }

            if (cmp <= 0)
            {
                visitor(n1.Key, n2.Key);
            }

            VisitKeyPairs(n1, n2.Right, visitor);
        }

        private int Compare(Node n, S key)
        {
            if (n is Phantom)
            {
                return -1;
            }
            else
            {
                return comparer(n.Key, key);
            }
        }

        private void Add(Node insertPos, S key, T value)
        {
            if (insertPos == null)
            {
                root = new Node(key, false);
                root.Value = value;
                ++numberOfKeys;
                return;
            }

            int cmp = Compare(insertPos, key);

            if (cmp == 0)
            {
                throw new Exception(string.Format("The key {0} was added twice", key));
            }
            else if (cmp > 0)
            {
                insertPos = new Node(insertPos, true, key, true);
            }
            else
            {
                insertPos = new Node(insertPos, false, key, true);
            }

            insertPos.Value = value;

            ++numberOfKeys;

            Node grand;
            Node psib;
            while (insertPos.Parent != null && insertPos.Parent.Red)
            {
                grand = GetGrandParent(insertPos);
                psib = GetParentsSibling(insertPos);
                if (psib != null && psib.Red)
                {
                    insertPos.Parent.Red = psib.Red = false;
                    grand.Red = true;
                    insertPos = grand;
                }
                else
                {
                    if (insertPos == insertPos.Parent.Right && insertPos.Parent == grand.Left)
                    {
                        LeftRotate(insertPos.Parent);
                        insertPos = insertPos.Left;
                        grand = GetGrandParent(insertPos);
                    }
                    else if (insertPos == insertPos.Parent.Left && insertPos.Parent == grand.Right)
                    {
                        RightRotate(insertPos.Parent);
                        insertPos = insertPos.Right;
                        grand = GetGrandParent(insertPos);
                    }

                    insertPos.Parent.Red = false;
                    grand.Red = true;
                    if (insertPos == insertPos.Parent.Left && insertPos.Parent == grand.Left)
                    {
                        RightRotate(grand);
                    }
                    else
                    {
                        LeftRotate(grand);
                    }

                    return;
                }
            }

            if (insertPos.Parent == null)
            {
                insertPos.Red = false;
            }
        }

        private int RegisterPhantom(Phantom n)
        {
            phantomList.AddLast(n);
            return phantomList.Count;
        }

        private Node GetGrandParent(Node n)
        {
            if (n.Parent == null)
            {
                return null;
            }

            return n.Parent.Parent;
        }

        private Node GetParentsSibling(Node n)
        {
            Node grand = GetGrandParent(n);
            if (grand == null)
            {
                return null;
            }
            else if (n.Parent == grand.Left)
            {
                return grand.Right;
            }
            else
            {
                return grand.Left;
            }
        }

        private Node GetSibling(Node n)
        {
            if (n.Parent == null)
            {
                return null;
            }
            else if (n == n.Parent.Left)
            {
                return n.Parent.Right;
            }
            else
            {
                return n.Parent.Left;
            }
        }

        private Node FindNearestNode(S key)
        {
            Node checkNode = root;
            int cmp;
            while (checkNode != null)
            {
                cmp = Compare(checkNode, key);
                if (cmp == 0)
                {
                    return checkNode;
                }
                else if (cmp > 0)
                {
                    if (checkNode.Left == null)
                    {
                        return checkNode;
                    }

                    checkNode = checkNode.Left;
                }
                else
                {
                    if (checkNode.Right == null)
                    {
                        return checkNode;
                    }

                    checkNode = checkNode.Right;
                }
            }

            return null;
        }

        private void LeftRotate(Node n)
        {
            Node nR = n.Right;
            Node nP = n.Parent;
            Node nRL = nR.Left;

            nR.Parent = nP;
            if (nP != null)
            {
                if (nP.Left == n)
                {
                    nP.Left = nR;
                }
                else
                {
                    nP.Right = nR;
                }
            }
            else
            {
                root = nR;
            }

            n.Right = nRL;
            if (nRL != null)
            {
                nRL.Parent = n;
            }

            nR.Left = n;
            n.Parent = nR;
        }

        private void RightRotate(Node n)
        {
            Node nL = n.Left;
            Node nP = n.Parent;
            Node nLR = nL.Right;

            nL.Parent = nP;
            if (nP != null)
            {
                if (nP.Left == n)
                {
                    nP.Left = nL;
                }
                else
                {
                    nP.Right = nL;
                }
            }
            else
            {
                root = nL;
            }

            n.Left = nLR;
            if (nLR != null)
            {
                nLR.Parent = n;
            }

            nL.Right = n;
            n.Parent = nL;
        }

        private void SwapNodes(Node u, Node v)
        {
            Node vParent = v.Parent;
            if (u.Parent != null && u.Parent == v)
            {
                bool onLeft = (vParent == null || vParent.Left == v) ? true : false;
                if (v.Left == u)
                {
                    Node parentRight = v.Right;
                    u.ReparentChildren(v);
                    u.Left = v;
                    u.Right = parentRight;
                    u.Parent = v.Parent;
                    v.Parent = u;
                    if (parentRight != null)
                    {
                        parentRight.Parent = u;
                    }
                }
                else
                {
                    Node parentLeft = v.Left;
                    u.ReparentChildren(v);
                    u.Right = v;
                    u.Left = parentLeft;
                    u.Parent = v.Parent;
                    v.Parent = u;
                    if (parentLeft != null)
                    {
                        parentLeft.Parent = u;
                    }
                }

                if (vParent == null)
                {
                    root = u;
                }
                else if (onLeft)
                {
                    vParent.Left = u;
                }
                else
                {
                    vParent.Right = u;
                }

                return;
            }
            else if (v.Parent != null && v.Parent == u)
            {
                SwapNodes(v, u);
                return;
            }

            Node uParent = u.Parent;
            Node uLeft = u.Left;
            Node uRight = u.Right;
            Node vLeft = v.Left;
            Node vRight = v.Right;

            if (uParent != null)
            {
                if (uParent.Left == u)
                {
                    uParent.Left = v;
                }
                else
                {
                    uParent.Right = v;
                }

                v.Parent = uParent;
            }
            else
            {
                v.Parent = null;
                root = v;
            }

            if (vParent != null)
            {
                if (vParent.Left == v)
                {
                    vParent.Left = u;
                }
                else
                {
                    vParent.Right = u;
                }

                u.Parent = vParent;
            }
            else
            {
                u.Parent = null;
                root = u;
            }

            u.AssignChildren(vLeft, vRight);
            v.AssignChildren(uLeft, uRight);
        }

        private Node BinaryTreeReplace(Node n)
        {
            Node rightmostLeftChild = n.Left;
            if (rightmostLeftChild == null)
            {
                return n;
            }

            while (rightmostLeftChild.Right != null)
            {
                rightmostLeftChild = rightmostLeftChild.Right;
            }

            var nKey = n.Key;
            var nVal = n.Value;
            n.Key = rightmostLeftChild.Key;
            n.Value = rightmostLeftChild.Value;

            rightmostLeftChild.Key = nKey;
            rightmostLeftChild.Value = nVal;
            return rightmostLeftChild;
        }

        private void ConstructSiblingFamily(Node n, ref Node sib, ref Node sibL, ref Node sibR)
        {
            sib = GetSibling(n);
            sib = (sib != null) ? sib : new Phantom(this, n.Parent, n.Parent.Right == n);
            sibL = (sib.Left != null) ? sib.Left : new Phantom(this, sib, true);
            sibR = (sib.Right != null) ? sib.Right : new Phantom(this, sib, false);
        }

        private void ClearPhantoms()
        {
            while (phantomList.Count > 0)
            {
                phantomList.First.Value.ClearPhantomNode(this);
                phantomList.RemoveFirst();
            }
        }

        private class Node
        {
            private S key;
            private Node left;
            private Node parent;
            private bool red;
            private Node right;
            private T value;

            public Node(S key, bool red)
            {
                this.key = key;
                this.red = red;
                left = right = parent = null;
            }

            public Node(Node parent, bool isLeftOfParent, S key, bool red)
            {
                this.key = key;
                this.red = red;
                this.parent = parent;
                if (isLeftOfParent)
                {
                    parent.left = this;
                }
                else
                {
                    parent.right = this;
                }

                left = right = null;
            }

            protected Node()
            {
//// Constructor used by phantoms
                key = default(S);
                red = false;
                left = right = null;
            }

            public S Key
            {
                get
                {
                    return key;
                }

                set
                {
                    key = value;
                }
            }

            public T Value
            {
                get
                {
                    return this.value;
                }

                set
                {
                    this.value = value;
                }
            }

            public bool Red
            {
                get
                {
                    return red;
                }

                set
                {
                    red = value;
                }
            }

            public Node Left
            {
                get
                {
                    return left;
                }

                set
                {
                    left = value;
                }
            }

            public Node Right
            {
                get
                {
                    return right;
                }

                set
                {
                    right = value;
                }
            }

            public Node Parent
            {
                get
                {
                    return parent;
                }

                set
                {
                    parent = value;
                }
            }

            public void ReparentChildren(Node p)
            {
                if (left != null)
                {
                    left.parent = p;
                }

                if (right != null)
                {
                    right.parent = p;
                }

                p.left = left;
                p.right = right;
                left = right = null;
            }

            public void AssignChildren(Node l, Node r)
            {
                if (l != null)
                {
                    l.parent = this;
                }

                if (r != null)
                {
                    r.parent = this;
                }

                left = l;
                right = r;
            }

            public void DiscardNode(Map<S, T> tree)
            {
                if (left != null)
                {
                    left.parent = null;
                }

                if (right != null)
                {
                    right.parent = null;
                }

                left = right = null;

                if (parent == null)
                {
                    tree.root = null;
                    return;
                }
                else if (parent.left == this)
                {
                    parent.left = null;
                }
                else
                {
                    parent.right = null;
                }

                parent = null;
            }

            public virtual bool IsPhantomNode()
            {
                return false;
            }

            public virtual void ClearPhantomNode(Map<S, T> tree)
            {
            }
        }

        private class Phantom : Node
        {
            public Phantom(Map<S, T> tree, Node parent, bool isLeftOfParent)
                : base()
            {
                if (isLeftOfParent)
                {
                    parent.Left = this;
                }
                else
                {
                    parent.Right = this;
                }

                this.Parent = parent;
                tree.RegisterPhantom(this);
            }

            public override bool IsPhantomNode()
            {
                return true;
            }

            public override void ClearPhantomNode(Map<S, T> tree)
            {
                if (Left != null)
                {
                    Left.Parent = null;
                }

                if (Right != null)
                {
                    Right.Parent = null;
                }

                Left = Right = null;

                if (Parent == null)
                {
                    tree.root = null;
                    return;
                }

                if (Parent.Left == this)
                {
                    Parent.Left = null;
                }
                else if (Parent.Right == this)
                {
                    Parent.Right = null;
                }

                Parent = null;
            }
        }
    }
}
